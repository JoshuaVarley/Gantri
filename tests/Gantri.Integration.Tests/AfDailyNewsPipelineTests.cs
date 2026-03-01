using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Mcp;
using Gantri.Abstractions.Plugins;
using Gantri.AI;
using Gantri.Bridge;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gantri.Integration.Tests;

/// <summary>
/// E2E #1: Tests the news-summarizer agent executing through AF's AIAgent
/// with brave MCP tools and file-save plugin tools.
/// </summary>
public class AfDailyNewsPipelineTests
{
    private static IHookPipeline CreateTrackingPipeline(List<string> firedEvents)
    {
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
        return pipeline;
    }

    [Fact]
    public async Task CreateAgent_WithPluginsAndMcp_CollectsAllTools()
    {
        var firedEvents = new List<string>();
        var pipeline = CreateTrackingPipeline(firedEvents);

        // Setup model provider
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

        // Setup plugin
        var plugin = Substitute.For<IPlugin>();
        plugin.Manifest.Returns(new PluginManifest
        {
            Name = "file-save",
            Version = "1.0.0",
            Description = "File save",
            Exports = new PluginExports
            {
                Actions = [new PluginActionExport { Name = "save", Description = "Save a file" }]
            }
        });

        var pluginRouter = Substitute.For<IPluginRouter>();
        pluginRouter.ResolveAsync("file-save", Arg.Any<CancellationToken>()).Returns(plugin);

        // Setup MCP
        var mcpToolProvider = Substitute.For<IMcpToolProvider>();
        mcpToolProvider.GetToolsAsync("brave", Arg.Any<CancellationToken>())
            .Returns(new List<McpToolInfo>
            {
                new() { ServerName = "brave", ToolName = "brave_news_search", Description = "Search news" }
            });

        // Mock chat client that returns a simple response
        var mockClient = Substitute.For<IChatClient>();
        mockClient.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new List<ChatMessage>
            {
                new(ChatRole.Assistant, "# Daily News Summary\n\nHere are today's top stories...")
            }));

        var factory = new GantriAgentFactory(
            registry, pluginRouter, mcpToolProvider, pipeline,
            NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()),
            Options.Create(new TelemetryOptions()),
            (_, _) => mockClient);

        var definition = new AgentDefinition
        {
            Name = "news-summarizer",
            Model = "gpt-5-mini",
            Provider = "azure-openai",
            Plugins = ["file-save"],
            McpServers = ["brave"],
            SystemPrompt = "You are a news summarizer."
        };

        // Create the AF agent
        var agent = await factory.CreateAgentAsync(definition);
        agent.Should().NotBeNull();
        agent.Name.Should().Be("news-summarizer");

        // Verify tools were collected
        await pluginRouter.Received(1).ResolveAsync("file-save", Arg.Any<CancellationToken>());
        await mcpToolProvider.Received(1).GetToolsAsync("brave", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfAgentSession_SendMessage_DelegatesToAfAgent()
    {
        var firedEvents = new List<string>();
        var pipeline = CreateTrackingPipeline(firedEvents);

        var registry = new ModelProviderRegistry();
        registry.RegisterProvider("test", new AiProviderOptions
        {
            ApiKey = "key",
            Endpoint = "https://test.openai.azure.com",
            Models = new Dictionary<string, AiModelOptions>
            {
                ["model"] = new() { Id = "gpt-4o" }
            }
        });

        var mockClient = Substitute.For<IChatClient>();
        mockClient.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new List<ChatMessage>
            {
                new(ChatRole.Assistant, "Here are the top news stories for today.")
            }));

        var factory = new GantriAgentFactory(
            registry, Substitute.For<IPluginRouter>(), Substitute.For<IMcpToolProvider>(),
            pipeline, NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()),
            Options.Create(new TelemetryOptions()),
            (_, _) => mockClient);

        var definition = new AgentDefinition
        {
            Name = "test-agent",
            Model = "model",
            Provider = "test",
            SystemPrompt = "You are a test agent."
        };

        var afAgent = await factory.CreateAgentAsync(definition);
        var session = await AfAgentSession.CreateAsync(afAgent, "test-agent", NullLogger<AfAgentSession>.Instance);

        var response = await session.SendMessageAsync("latest tech news");

        response.Should().Contain("top news stories");

        // Verify hooks were fired (before and after model-call)
        firedEvents.Should().Contain(e => e.Contains("model-call") && e.Contains("before"));
        firedEvents.Should().Contain(e => e.Contains("model-call") && e.Contains("after"));
    }
}
