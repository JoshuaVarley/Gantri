using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Plugins;
using Gantri.Configuration;
using Gantri.Workflows;
using Gantri.Workflows.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Workflows.Tests;

public class WorkflowEngineExtendedTests : IDisposable
{
    private readonly string _tempDir;

    public WorkflowEngineExtendedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-wf-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

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

    private (WorkflowEngine engine, IAgentOrchestrator orchestrator) CreateEngine(
        Dictionary<string, WorkflowDefinition> workflows,
        WorkflowStateManager? stateManager = null)
    {
        var orchestrator = Substitute.For<IAgentOrchestrator>();
        var pluginRouter = Substitute.For<IPluginRouter>();
        var pipeline = CreatePassthroughPipeline();

        var agentHandler = new AgentStepHandler(orchestrator);
        var pluginHandler = new PluginStepHandler(pluginRouter);
        var conditionHandler = new ConditionStepHandler();
        var approvalHandler = new ApprovalStepHandler(NullLogger<ApprovalStepHandler>.Instance);

        var stepExecutor = new StepExecutor(
            [agentHandler, pluginHandler, conditionHandler, approvalHandler],
            pipeline,
            NullLogger<StepExecutor>.Instance);

        var parallelHandler = new ParallelStepHandler(() => stepExecutor);

        var fullExecutor = new StepExecutor(
            [agentHandler, pluginHandler, conditionHandler, approvalHandler, parallelHandler],
            pipeline,
            NullLogger<StepExecutor>.Instance);

        var engine = new WorkflowEngine(
            new WorkflowDefinitionRegistry(workflows),
            fullExecutor,
            pipeline,
            NullLogger<WorkflowEngine>.Instance,
            stateManager);

        return (engine, orchestrator);
    }

    [Fact]
    public async Task ExecuteAsync_ApprovalStep_PausesWorkflow()
    {
        var stateManager = new WorkflowStateManager(_tempDir, NullLogger<WorkflowStateManager>.Instance);

        var workflows = new Dictionary<string, WorkflowDefinition>
        {
            ["deploy-wf"] = new()
            {
                Name = "deploy-wf",
                Steps =
                [
                    new WorkflowStepDefinition
                    {
                        Id = "approve",
                        Type = "approval",
                        Input = "Approve deployment"
                    },
                    new WorkflowStepDefinition
                    {
                        Id = "deploy",
                        Type = "agent",
                        Agent = "deployer"
                    }
                ]
            }
        };

        var (engine, _) = CreateEngine(workflows, stateManager);

        var result = await engine.ExecuteAsync("deploy-wf");

        result.Success.Should().BeTrue();
        result.FinalOutput.Should().Contain("approval");
        result.ExecutionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithStateManager_SetsExecutionId()
    {
        var stateManager = new WorkflowStateManager(_tempDir, NullLogger<WorkflowStateManager>.Instance);
        var orchestrator = Substitute.For<IAgentOrchestrator>();

        var mockSession = Substitute.For<IAgentSession>();
        mockSession.SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("done");
        mockSession.AgentName.Returns("test-agent");
        mockSession.SessionId.Returns("s1");

        orchestrator.CreateSessionAsync("test-agent", Arg.Any<CancellationToken>())
            .Returns(mockSession);

        var workflows = new Dictionary<string, WorkflowDefinition>
        {
            ["simple"] = new()
            {
                Name = "simple",
                Steps =
                [
                    new WorkflowStepDefinition
                    {
                        Id = "step1",
                        Type = "agent",
                        Agent = "test-agent",
                        Input = "go"
                    }
                ]
            }
        };

        var pluginRouter = Substitute.For<IPluginRouter>();
        var pipeline = CreatePassthroughPipeline();
        var agentHandler = new AgentStepHandler(orchestrator);
        var pluginHandler = new PluginStepHandler(pluginRouter);
        var conditionHandler = new ConditionStepHandler();
        var approvalHandler = new ApprovalStepHandler(NullLogger<ApprovalStepHandler>.Instance);

        var executor = new StepExecutor(
            [agentHandler, pluginHandler, conditionHandler, approvalHandler],
            pipeline,
            NullLogger<StepExecutor>.Instance);

        var engine = new WorkflowEngine(
            new WorkflowDefinitionRegistry(workflows),
            executor,
            pipeline,
            NullLogger<WorkflowEngine>.Instance,
            stateManager);

        var result = await engine.ExecuteAsync("simple");

        result.Success.Should().BeTrue();
        result.ExecutionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetRunStatusAsync_ReturnsStatus()
    {
        var stateManager = new WorkflowStateManager(_tempDir, NullLogger<WorkflowStateManager>.Instance);

        var workflows = new Dictionary<string, WorkflowDefinition>
        {
            ["test-wf"] = new()
            {
                Name = "test-wf",
                Steps =
                [
                    new WorkflowStepDefinition
                    {
                        Id = "gate",
                        Type = "approval",
                        Input = "Approve"
                    },
                    new WorkflowStepDefinition
                    {
                        Id = "after-gate",
                        Type = "agent",
                        Agent = "test-agent"
                    }
                ]
            }
        };

        var (engine, _) = CreateEngine(workflows, stateManager);

        // Execute — should pause at approval gate
        var result = await engine.ExecuteAsync("test-wf");
        var execId = result.ExecutionId;

        var status = await engine.GetRunStatusAsync(execId!);
        status.Should().NotBeNull();
        status!.ExecutionId.Should().Be(execId);
        status.WorkflowName.Should().Be("test-wf");
        status.Status.Should().Be("waiting_approval");
        status.TotalSteps.Should().Be(2);
    }

    [Fact]
    public async Task GetRunStatusAsync_UnknownId_ReturnsNull()
    {
        var stateManager = new WorkflowStateManager(_tempDir, NullLogger<WorkflowStateManager>.Instance);
        var workflows = new Dictionary<string, WorkflowDefinition>();
        var (engine, _) = CreateEngine(workflows, stateManager);

        var status = await engine.GetRunStatusAsync("nonexistent");
        status.Should().BeNull();
    }

    [Fact]
    public async Task GetRunStatusAsync_NoStateManager_ReturnsNull()
    {
        var workflows = new Dictionary<string, WorkflowDefinition>();
        var (engine, _) = CreateEngine(workflows, stateManager: null);

        var status = await engine.GetRunStatusAsync("anything");
        status.Should().BeNull();
    }

    [Fact]
    public async Task ListActiveRunsAsync_NoStateManager_ReturnsEmpty()
    {
        var workflows = new Dictionary<string, WorkflowDefinition>();
        var (engine, _) = CreateEngine(workflows, stateManager: null);

        var runs = await engine.ListActiveRunsAsync();
        runs.Should().BeEmpty();
    }

    [Fact]
    public async Task ResumeAsync_NoStateManager_Throws()
    {
        var workflows = new Dictionary<string, WorkflowDefinition>();
        var (engine, _) = CreateEngine(workflows, stateManager: null);

        var act = () => engine.ResumeAsync("any-id");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not configured*");
    }

    [Fact]
    public async Task ResumeAsync_UnknownExecution_Throws()
    {
        var stateManager = new WorkflowStateManager(_tempDir, NullLogger<WorkflowStateManager>.Instance);
        var workflows = new Dictionary<string, WorkflowDefinition>();
        var (engine, _) = CreateEngine(workflows, stateManager);

        var act = () => engine.ResumeAsync("nonexistent");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No saved state*");
    }

    [Fact]
    public async Task ExecuteAsync_FailedStep_SetsFailedState()
    {
        var stateManager = new WorkflowStateManager(_tempDir, NullLogger<WorkflowStateManager>.Instance);

        var orchestrator = Substitute.For<IAgentOrchestrator>();
        orchestrator.CreateSessionAsync("bad-agent", Arg.Any<CancellationToken>())
            .Returns<IAgentSession>(x => throw new InvalidOperationException("Agent not found"));

        var pluginRouter = Substitute.For<IPluginRouter>();
        var pipeline = CreatePassthroughPipeline();
        var agentHandler = new AgentStepHandler(orchestrator);
        var pluginHandler = new PluginStepHandler(pluginRouter);
        var conditionHandler = new ConditionStepHandler();
        var approvalHandler = new ApprovalStepHandler(NullLogger<ApprovalStepHandler>.Instance);

        var executor = new StepExecutor(
            [agentHandler, pluginHandler, conditionHandler, approvalHandler],
            pipeline,
            NullLogger<StepExecutor>.Instance);

        var workflows = new Dictionary<string, WorkflowDefinition>
        {
            ["fail-wf"] = new()
            {
                Name = "fail-wf",
                Steps =
                [
                    new WorkflowStepDefinition
                    {
                        Id = "bad-step",
                        Type = "agent",
                        Agent = "bad-agent"
                    }
                ]
            }
        };

        var engine = new WorkflowEngine(
            new WorkflowDefinitionRegistry(workflows), executor, pipeline,
            NullLogger<WorkflowEngine>.Instance,
            stateManager);

        var result = await engine.ExecuteAsync("fail-wf");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("bad-step");
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_ReturnsFailure()
    {
        var stateManager = new WorkflowStateManager(_tempDir, NullLogger<WorkflowStateManager>.Instance);

        var orchestrator = Substitute.For<IAgentOrchestrator>();
        orchestrator.CreateSessionAsync("slow-agent", Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.ArgAt<CancellationToken>(1);
                await Task.Delay(10000, ct); // Will be cancelled
                return Substitute.For<IAgentSession>();
            });

        var pluginRouter = Substitute.For<IPluginRouter>();
        var pipeline = CreatePassthroughPipeline();
        var agentHandler = new AgentStepHandler(orchestrator);
        var executor = new StepExecutor(
            [agentHandler],
            pipeline,
            NullLogger<StepExecutor>.Instance);

        var workflows = new Dictionary<string, WorkflowDefinition>
        {
            ["slow-wf"] = new()
            {
                Name = "slow-wf",
                Steps =
                [
                    new WorkflowStepDefinition
                    {
                        Id = "slow-step",
                        Type = "agent",
                        Agent = "slow-agent"
                    }
                ]
            }
        };

        var engine = new WorkflowEngine(
            new WorkflowDefinitionRegistry(workflows), executor, pipeline,
            NullLogger<WorkflowEngine>.Instance,
            stateManager);

        using var cts = new CancellationTokenSource(100); // Cancel after 100ms
        var result = await engine.ExecuteAsync("slow-wf", cancellationToken: cts.Token);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("cancelled");
    }

    [Fact]
    public async Task ExecuteAsync_ParallelSteps_AllComplete()
    {
        var orchestrator = Substitute.For<IAgentOrchestrator>();

        var session1 = Substitute.For<IAgentSession>();
        session1.SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("out-1");
        session1.AgentName.Returns("agent-a");
        session1.SessionId.Returns("s1");

        var session2 = Substitute.For<IAgentSession>();
        session2.SendMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("out-2");
        session2.AgentName.Returns("agent-b");
        session2.SessionId.Returns("s2");

        orchestrator.CreateSessionAsync("agent-a", Arg.Any<CancellationToken>()).Returns(session1);
        orchestrator.CreateSessionAsync("agent-b", Arg.Any<CancellationToken>()).Returns(session2);

        var workflows = new Dictionary<string, WorkflowDefinition>
        {
            ["parallel-wf"] = new()
            {
                Name = "parallel-wf",
                Steps =
                [
                    new WorkflowStepDefinition
                    {
                        Id = "parallel-gate",
                        Type = "parallel",
                        Steps =
                        [
                            new WorkflowStepDefinition
                            {
                                Id = "sub-a",
                                Type = "agent",
                                Agent = "agent-a",
                                Input = "run-a"
                            },
                            new WorkflowStepDefinition
                            {
                                Id = "sub-b",
                                Type = "agent",
                                Agent = "agent-b",
                                Input = "run-b"
                            }
                        ]
                    }
                ]
            }
        };

        var (engine, _) = CreateEngine(workflows);

        // Override the orchestrator
        var pluginRouter = Substitute.For<IPluginRouter>();
        var pipeline = CreatePassthroughPipeline();
        var agentHandler = new AgentStepHandler(orchestrator);
        var pluginHandler = new PluginStepHandler(pluginRouter);
        var conditionHandler = new ConditionStepHandler();
        var approvalHandler = new ApprovalStepHandler(NullLogger<ApprovalStepHandler>.Instance);

        var stepExecutor = new StepExecutor(
            [agentHandler, pluginHandler, conditionHandler, approvalHandler],
            pipeline,
            NullLogger<StepExecutor>.Instance);
        var parallelHandler = new ParallelStepHandler(() => stepExecutor);
        var fullExecutor = new StepExecutor(
            [agentHandler, pluginHandler, conditionHandler, approvalHandler, parallelHandler],
            pipeline,
            NullLogger<StepExecutor>.Instance);

        var realEngine = new WorkflowEngine(
            new WorkflowDefinitionRegistry(workflows), fullExecutor, pipeline,
            NullLogger<WorkflowEngine>.Instance);

        var result = await realEngine.ExecuteAsync("parallel-wf");

        result.Success.Should().BeTrue();
        result.StepOutputs.Should().ContainKey("sub-a");
        result.StepOutputs.Should().ContainKey("sub-b");
    }
}
