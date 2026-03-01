using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Mcp;
using Gantri.Abstractions.Plugins;
using Gantri.AI;
using Gantri.Bridge;
using Gantri.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gantri.Integration.Tests;

/// <summary>
/// E2E #3: Three agents (content-writer, fact-checker, editor) collaborate via AF native group chat workflow.
/// A hook monitors all agent:*:model-call:before events.
/// </summary>
public class GroupChatContentReviewTests
{
    [Fact]
    public async Task GroupChat_ThreeAgents_ExecutesViaAfWorkflow()
    {
        var firedEvents = new List<string>();
        var pipeline = Substitute.For<IHookPipeline>();
        pipeline.ExecuteAsync(
            Arg.Any<HookEvent>(),
            Arg.Any<Func<HookContext, ValueTask>>(),
            Arg.Any<HookContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var evt = callInfo.ArgAt<HookEvent>(0);
                firedEvents.Add(evt.Pattern);
                var ctx = new HookContext(evt);
                return new ValueTask<HookContext>(ctx);
            });

        var registry = new ModelProviderRegistry();
        registry.RegisterProvider("azure-openai", new AiProviderOptions
        {
            ApiKey = "test-key",
            Endpoint = "https://test.openai.azure.com",
            Models = new Dictionary<string, AiModelOptions>
            {
                ["gpt-5-mini"] = new() { Id = "gpt-5-mini" }
            }
        });

        // Track which agents get called
        var agentCallOrder = new List<string>();

        var mockClient = Substitute.For<IChatClient>();

        // Helper to identify agent from instructions and return appropriate response
        string IdentifyAndRespond(ChatOptions? options, IEnumerable<ChatMessage> messages)
        {
            var instructions = options?.Instructions
                ?? messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text
                ?? "";

            if (instructions.Contains("content writer", StringComparison.OrdinalIgnoreCase))
            {
                agentCallOrder.Add("content-writer");
                return "# Draft: AI Agents\n\nAI agents are revolutionizing software development...";
            }
            else if (instructions.Contains("fact-checker", StringComparison.OrdinalIgnoreCase))
            {
                agentCallOrder.Add("fact-checker");
                return "Fact check: All claims verified. No issues found.";
            }
            else if (instructions.Contains("editor", StringComparison.OrdinalIgnoreCase))
            {
                agentCallOrder.Add("editor");
                return "# AI Agents: A Comprehensive Overview\n\nAI agents are transforming the industry...";
            }
            else
            {
                agentCallOrder.Add("unknown");
                return "Response";
            }
        }

        mockClient.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var msgs = callInfo.ArgAt<IEnumerable<ChatMessage>>(0).ToList();
                var opts = callInfo.ArgAt<ChatOptions?>(1);
                var response = IdentifyAndRespond(opts, msgs);
                return Task.FromResult(new ChatResponse(new List<ChatMessage>
                {
                    new(ChatRole.Assistant, response)
                }));
            });

        // AF Workflows uses the streaming path internally even for non-streaming RunAsync
        mockClient.GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var msgs = callInfo.ArgAt<IEnumerable<ChatMessage>>(0).ToList();
                var opts = callInfo.ArgAt<ChatOptions?>(1);
                var response = IdentifyAndRespond(opts, msgs);
                var updates = new List<ChatResponseUpdate>
                {
                    new(ChatRole.Assistant, response)
                };
                return updates.ToAsyncEnumerable();
            });

        var pluginRouter = Substitute.For<IPluginRouter>();
        var mcpToolProvider = Substitute.For<IMcpToolProvider>();

        var factory = new GantriAgentFactory(
            registry, pluginRouter, mcpToolProvider, pipeline,
            NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()),
            Options.Create(new TelemetryOptions()),
            (_, _) => mockClient);

        var definitions = new Dictionary<string, AgentDefinition>
        {
            ["content-writer"] = new()
            {
                Name = "content-writer",
                Model = "gpt-5-mini",
                Provider = "azure-openai",
                SystemPrompt = "You are a content writer."
            },
            ["fact-checker"] = new()
            {
                Name = "fact-checker",
                Model = "gpt-5-mini",
                Provider = "azure-openai",
                SystemPrompt = "You are a fact-checker."
            },
            ["editor"] = new()
            {
                Name = "editor",
                Model = "gpt-5-mini",
                Provider = "azure-openai",
                SystemPrompt = "You are an editor."
            }
        };

        var orchestrator = new AfAgentOrchestrator(
            factory, pipeline, new AgentDefinitionRegistry(definitions),
            NullLoggerFactory.Instance);

        var result = await orchestrator.RunGroupChatAsync(
            ["content-writer", "fact-checker", "editor"],
            "Write about AI agents",
            maxIterations: 3);

        // All 3 agents should have been invoked
        agentCallOrder.Should().HaveCountGreaterThanOrEqualTo(3);
        agentCallOrder.Should().Contain("content-writer");
        agentCallOrder.Should().Contain("fact-checker");
        agentCallOrder.Should().Contain("editor");

        // Final output should be from the editor
        result.Should().Contain("Comprehensive Overview");

        // Hook events should contain model-call:before for each agent
        var modelCallBeforeEvents = firedEvents
            .Where(e => e.Contains("model-call") && e.Contains("before"))
            .ToList();
        modelCallBeforeEvents.Should().HaveCountGreaterThanOrEqualTo(3);

        // Group chat lifecycle hooks should have fired
        firedEvents.Should().Contain(e => e.Contains("group-chat") && e.Contains("start"));
        firedEvents.Should().Contain(e => e.Contains("group-chat") && e.Contains("end"));
    }

    [Fact]
    public async Task GroupChat_UnknownAgent_Throws()
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

        var registry = new ModelProviderRegistry();
        var factory = new GantriAgentFactory(
            registry, Substitute.For<IPluginRouter>(), Substitute.For<IMcpToolProvider>(),
            pipeline, NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()),
            Options.Create(new TelemetryOptions()));

        var orchestrator = new AfAgentOrchestrator(
            factory, pipeline, new AgentDefinitionRegistry(),
            NullLoggerFactory.Instance);

        var act = () => orchestrator.RunGroupChatAsync(
            ["nonexistent"], "hello", cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }
}
