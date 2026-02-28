using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Mcp;
using Gantri.Abstractions.Plugins;
using Gantri.Abstractions.Workflows;
using Gantri.AI;
using Gantri.Bridge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gantri.Bridge.Tests;

public class AfWorkflowEngineRoutingTests
{
    private readonly ILegacyWorkflowEngine _legacyEngine;
    private readonly IWorkflowDefinitionRegistry _workflowRegistry;
    private readonly IAgentDefinitionRegistry _agentRegistry;
    private readonly AfWorkflowEngine _sut;

    public AfWorkflowEngineRoutingTests()
    {
        _legacyEngine = Substitute.For<ILegacyWorkflowEngine>();
        _workflowRegistry = Substitute.For<IWorkflowDefinitionRegistry>();
        _agentRegistry = Substitute.For<IAgentDefinitionRegistry>();

        // Create a real GantriAgentFactory with mock dependencies (it's sealed, can't substitute)
        var modelRegistry = new ModelProviderRegistry();
        var agentFactory = new GantriAgentFactory(
            modelRegistry,
            Substitute.For<IPluginRouter>(),
            Substitute.For<IMcpToolProvider>(),
            Substitute.For<IHookPipeline>(),
            NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions())
        );

        _sut = new AfWorkflowEngine(
            _legacyEngine,
            agentFactory,
            _workflowRegistry,
            NullLogger<AfWorkflowEngine>.Instance,
            _agentRegistry
        );
    }

    [Fact]
    public async Task AgentOnlySteps_RouteThroughAf()
    {
        // All steps are agent-type with no sub-steps, so CanExecuteViaAf returns true.
        var definition = new WorkflowDefinition
        {
            Name = "agent-only",
            Steps =
            [
                new WorkflowStepDefinition
                {
                    Id = "step1",
                    Type = "agent",
                    Agent = "summarizer",
                },
                new WorkflowStepDefinition
                {
                    Id = "step2",
                    Type = "agent",
                    Agent = "reviewer",
                },
            ],
        };

        _workflowRegistry.TryGet("agent-only").Returns(definition);

        // The AF path will try to create agents via the real factory, which will fail
        // because no model provider is registered. But the key assertion is that
        // the legacy engine was NOT called — proving the routing went to AF.
        var result = await _sut.ExecuteAsync("agent-only");

        // AF path catches internal exceptions and returns a failure WorkflowResult
        result.Success.Should().BeFalse();
        await _legacyEngine
            .DidNotReceive()
            .ExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task MixedSteps_RoutesThroughLegacy()
    {
        // Mixed agent + parallel steps means CanExecuteViaAf is false; routes to legacy.
        var definition = new WorkflowDefinition
        {
            Name = "mixed",
            Steps =
            [
                new WorkflowStepDefinition
                {
                    Id = "step1",
                    Type = "agent",
                    Agent = "summarizer",
                },
                new WorkflowStepDefinition
                {
                    Id = "step2",
                    Type = "parallel",
                    Steps =
                    [
                        new WorkflowStepDefinition
                        {
                            Id = "sub1",
                            Type = "agent",
                            Agent = "a",
                        },
                        new WorkflowStepDefinition
                        {
                            Id = "sub2",
                            Type = "agent",
                            Agent = "b",
                        },
                    ],
                },
            ],
        };

        _workflowRegistry.TryGet("mixed").Returns(definition);
        _legacyEngine
            .ExecuteAsync("mixed", null, Arg.Any<CancellationToken>())
            .Returns(WorkflowResult.Ok(new Dictionary<string, object?>(), "done", TimeSpan.Zero));

        var result = await _sut.ExecuteAsync("mixed");

        result.Success.Should().BeTrue();
        await _legacyEngine.Received(1).ExecuteAsync("mixed", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyWorkflow_RoutesThroughLegacy()
    {
        var definition = new WorkflowDefinition { Name = "empty", Steps = [] };

        _workflowRegistry.TryGet("empty").Returns(definition);
        _legacyEngine
            .ExecuteAsync("empty", null, Arg.Any<CancellationToken>())
            .Returns(WorkflowResult.Ok(new Dictionary<string, object?>(), null, TimeSpan.Zero));

        await _sut.ExecuteAsync("empty");

        await _legacyEngine.Received(1).ExecuteAsync("empty", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConditionStep_RoutesThroughLegacy()
    {
        var definition = new WorkflowDefinition
        {
            Name = "conditional",
            Steps =
            [
                new WorkflowStepDefinition
                {
                    Id = "step1",
                    Type = "condition",
                    Condition = "x > 0",
                },
            ],
        };

        _workflowRegistry.TryGet("conditional").Returns(definition);
        _legacyEngine
            .ExecuteAsync("conditional", null, Arg.Any<CancellationToken>())
            .Returns(WorkflowResult.Ok(new Dictionary<string, object?>(), null, TimeSpan.Zero));

        await _sut.ExecuteAsync("conditional");

        await _legacyEngine
            .Received(1)
            .ExecuteAsync("conditional", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PluginStep_RoutesThroughLegacy()
    {
        var definition = new WorkflowDefinition
        {
            Name = "plugin-wf",
            Steps =
            [
                new WorkflowStepDefinition
                {
                    Id = "step1",
                    Type = "plugin",
                    Plugin = "file-save",
                    Action = "save",
                },
            ],
        };

        _workflowRegistry.TryGet("plugin-wf").Returns(definition);
        _legacyEngine
            .ExecuteAsync("plugin-wf", null, Arg.Any<CancellationToken>())
            .Returns(WorkflowResult.Ok(new Dictionary<string, object?>(), null, TimeSpan.Zero));

        await _sut.ExecuteAsync("plugin-wf");

        await _legacyEngine
            .Received(1)
            .ExecuteAsync("plugin-wf", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApprovalStep_RoutesThroughLegacy()
    {
        var definition = new WorkflowDefinition
        {
            Name = "approval-wf",
            Steps = [new WorkflowStepDefinition { Id = "step1", Type = "approval" }],
        };

        _workflowRegistry.TryGet("approval-wf").Returns(definition);
        _legacyEngine
            .ExecuteAsync("approval-wf", null, Arg.Any<CancellationToken>())
            .Returns(WorkflowResult.Ok(new Dictionary<string, object?>(), null, TimeSpan.Zero));

        await _sut.ExecuteAsync("approval-wf");

        await _legacyEngine
            .Received(1)
            .ExecuteAsync("approval-wf", null, Arg.Any<CancellationToken>());
    }
}
