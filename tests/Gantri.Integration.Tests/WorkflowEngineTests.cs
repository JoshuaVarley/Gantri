using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Plugins;
using Gantri.Configuration;
using Gantri.Workflows;
using Gantri.Workflows.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Integration.Tests;

public class WorkflowEngineTests
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

    private static (WorkflowEngine engine, IAgentOrchestrator orchestrator) CreateEngine(
        Dictionary<string, WorkflowDefinition> workflows)
    {
        var orchestrator = Substitute.For<IAgentOrchestrator>();
        var pluginRouter = Substitute.For<IPluginRouter>();
        var pipeline = CreatePassthroughPipeline();

        var agentHandler = new AgentStepHandler(orchestrator);
        var pluginHandler = new PluginStepHandler(pluginRouter);
        var conditionHandler = new ConditionStepHandler();

        var stepExecutor = new StepExecutor(
            [agentHandler, pluginHandler, conditionHandler],
            pipeline,
            NullLogger<StepExecutor>.Instance);

        var parallelHandler = new ParallelStepHandler(() => stepExecutor);

        // Re-create with all handlers including parallel
        var fullExecutor = new StepExecutor(
            [agentHandler, pluginHandler, conditionHandler, parallelHandler],
            pipeline,
            NullLogger<StepExecutor>.Instance);

        var engine = new WorkflowEngine(
            new WorkflowDefinitionRegistry(workflows),
            fullExecutor,
            pipeline,
            NullLogger<WorkflowEngine>.Instance);

        return (engine, orchestrator);
    }

    [Fact]
    public async Task ExecuteWorkflow_SingleAgentStep_CompletesSuccessfully()
    {
        var workflows = new Dictionary<string, WorkflowDefinition>
        {
            ["test-workflow"] = new()
            {
                Name = "test-workflow",
                Steps =
                [
                    new WorkflowStepDefinition
                    {
                        Id = "step1",
                        Type = "agent",
                        Agent = "test-agent",
                        Input = "${input.text}"
                    }
                ]
            }
        };

        var (engine, orchestrator) = CreateEngine(workflows);

        var mockSession = Substitute.For<IAgentSession>();
        mockSession.SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Agent response");
        mockSession.AgentName.Returns("test-agent");
        mockSession.SessionId.Returns("abc123");

        orchestrator.CreateSessionAsync("test-agent", Arg.Any<CancellationToken>())
            .Returns(mockSession);

        var result = await engine.ExecuteAsync("test-workflow",
            new Dictionary<string, object?> { ["text"] = "Hello" });

        result.Success.Should().BeTrue();
        result.FinalOutput.Should().Be("Agent response");
        result.StepOutputs.Should().ContainKey("step1");
    }

    [Fact]
    public async Task ExecuteWorkflow_MultipleSteps_ChainsProperly()
    {
        var workflows = new Dictionary<string, WorkflowDefinition>
        {
            ["chain"] = new()
            {
                Name = "chain",
                Steps =
                [
                    new WorkflowStepDefinition
                    {
                        Id = "review",
                        Type = "agent",
                        Agent = "reviewer",
                        Input = "Review: ${input.text}"
                    },
                    new WorkflowStepDefinition
                    {
                        Id = "triage",
                        Type = "agent",
                        Agent = "triager",
                        Input = "Triage: ${steps.review.output}"
                    }
                ]
            }
        };

        var (engine, orchestrator) = CreateEngine(workflows);

        var reviewSession = Substitute.For<IAgentSession>();
        reviewSession.SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Found 3 bugs");
        reviewSession.AgentName.Returns("reviewer");
        reviewSession.SessionId.Returns("s1");

        var triageSession = Substitute.For<IAgentSession>();
        triageSession.SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Priority: P1");
        triageSession.AgentName.Returns("triager");
        triageSession.SessionId.Returns("s2");

        orchestrator.CreateSessionAsync("reviewer", Arg.Any<CancellationToken>())
            .Returns(reviewSession);
        orchestrator.CreateSessionAsync("triager", Arg.Any<CancellationToken>())
            .Returns(triageSession);

        var result = await engine.ExecuteAsync("chain",
            new Dictionary<string, object?> { ["text"] = "my code" });

        result.Success.Should().BeTrue();
        result.StepOutputs["review"].Should().Be("Found 3 bugs");
        result.StepOutputs["triage"].Should().Be("Priority: P1");
        result.FinalOutput.Should().Be("Priority: P1");
    }

    [Fact]
    public async Task ExecuteWorkflow_ConditionStep_EvaluatesCorrectly()
    {
        var workflows = new Dictionary<string, WorkflowDefinition>
        {
            ["conditional"] = new()
            {
                Name = "conditional",
                Steps =
                [
                    new WorkflowStepDefinition
                    {
                        Id = "check",
                        Type = "condition",
                        Condition = "${input.flag}"
                    }
                ]
            }
        };

        var (engine, _) = CreateEngine(workflows);

        var trueResult = await engine.ExecuteAsync("conditional",
            new Dictionary<string, object?> { ["flag"] = "true" });
        trueResult.Success.Should().BeTrue();
        trueResult.StepOutputs["check"].Should().Be(true);

        var falseResult = await engine.ExecuteAsync("conditional",
            new Dictionary<string, object?> { ["flag"] = "false" });
        falseResult.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWorkflow_UnknownWorkflow_Throws()
    {
        var (engine, _) = CreateEngine(new Dictionary<string, WorkflowDefinition>());

        var act = () => engine.ExecuteAsync("nonexistent");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void ListWorkflows_ReturnsConfigured()
    {
        var workflows = new Dictionary<string, WorkflowDefinition>
        {
            ["wf-a"] = new() { Name = "wf-a" },
            ["wf-b"] = new() { Name = "wf-b" }
        };

        var (engine, _) = CreateEngine(workflows);
        var list = engine.ListWorkflows();
        list.Should().HaveCount(2);
        list.Should().Contain("wf-a");
        list.Should().Contain("wf-b");
    }
}
