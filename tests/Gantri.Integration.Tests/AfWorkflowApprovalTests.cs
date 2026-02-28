using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Workflows;
using Gantri.Bridge;
using Gantri.Configuration;
using Gantri.Workflows;
using Gantri.Workflows.Steps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Integration.Tests;

/// <summary>
/// E2E #2: Tests multi-step workflow with approval gate.
/// 3-step workflow: agent → approval → agent. Pauses at approval gate, persists state, resumes.
/// </summary>
public class AfWorkflowApprovalTests
{
    private static IHookPipeline CreatePassthroughPipeline()
    {
        var pipeline = Substitute.For<IHookPipeline>();
        pipeline.ExecuteAsync(
            Arg.Any<HookEvent>(),
            Arg.Any<Func<HookContext, ValueTask>>(),
            Arg.Any<HookContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ctx = new HookContext(callInfo.ArgAt<HookEvent>(0));
                return new ValueTask<HookContext>(ctx);
            });
        return pipeline;
    }

    [Fact]
    public async Task WorkflowWithApproval_PausesAndResumes()
    {
        // Setup orchestrator mock for agent steps
        var orchestrator = Substitute.For<IAgentOrchestrator>();
        orchestrator.CreateSessionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var session = Substitute.For<IAgentSession>();
                session.AgentName.Returns(callInfo.ArgAt<string>(0));
                session.SessionId.Returns(Guid.NewGuid().ToString("N")[..12]);
                session.SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns("Agent response for step");
                return session;
            });

        var pipeline = CreatePassthroughPipeline();

        // Build step handlers
        var stepHandlers = new List<IStepHandler>
        {
            new AgentStepHandler(orchestrator),
            new ApprovalStepHandler(NullLogger<ApprovalStepHandler>.Instance),
        };
        var stepExecutor = new StepExecutor(stepHandlers, pipeline, NullLogger<StepExecutor>.Instance);

        // Use temp directory for state persistence
        var tempDir = Path.Combine(Path.GetTempPath(), $"gantri-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var stateManager = new WorkflowStateManager(tempDir,
                NullLogger<WorkflowStateManager>.Instance);

            var definitions = new Dictionary<string, WorkflowDefinition>
            {
                ["test-approval"] = new WorkflowDefinition
                {
                    Name = "test-approval",
                    Steps =
                    [
                        new() { Id = "step1", Type = "agent", Agent = "writer", Input = "Write content" },
                        new() { Id = "approval", Type = "approval", Input = "Approve the content" },
                        new() { Id = "step3", Type = "agent", Agent = "editor", Input = "Edit: ${steps.step1.output}" }
                    ]
                }
            };

            var engine = new WorkflowEngine(new WorkflowDefinitionRegistry(definitions), stepExecutor, pipeline,
                NullLogger<WorkflowEngine>.Instance, stateManager);

            // Execute — should pause at approval gate
            var result = await engine.ExecuteAsync("test-approval");

            result.Success.Should().BeTrue();
            result.FinalOutput.Should().Contain("approval");
            result.ExecutionId.Should().NotBeNullOrEmpty();

            // Verify state was persisted
            var activeRuns = await engine.ListActiveRunsAsync();
            activeRuns.Should().HaveCount(1);
            activeRuns[0].Status.Should().Be("waiting_approval");

            // Resume the workflow
            var resumeResult = await engine.ResumeAsync(result.ExecutionId!);

            resumeResult.Success.Should().BeTrue();
            resumeResult.StepOutputs.Should().ContainKey("step3");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
