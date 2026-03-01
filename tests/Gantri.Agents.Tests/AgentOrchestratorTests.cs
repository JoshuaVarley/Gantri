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

namespace Gantri.Agents.Tests;

public class AgentOrchestratorTests
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

    private static GantriAgentFactory CreateFactory(IChatClient? mockClient = null)
    {
        var registry = new ModelProviderRegistry();
        registry.RegisterProvider("anthropic", new AiProviderOptions
        {
            ApiKey = "test-key",
            Endpoint = "https://test.openai.azure.com",
            Models = new Dictionary<string, AiModelOptions>
            {
                ["sonnet"] = new() { Id = "sonnet" },
                ["haiku"] = new() { Id = "haiku" }
            }
        });

        var client = mockClient ?? Substitute.For<IChatClient>();
        Func<string, AiModelOptions, IChatClient> clientFactory = (_, _) => client;

        return new GantriAgentFactory(
            registry,
            Substitute.For<IPluginRouter>(),
            Substitute.For<IMcpToolProvider>(),
            CreatePassthroughPipeline(),
            NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()),
            Options.Create(new TelemetryOptions()),
            clientFactory);
    }

    [Fact]
    public async Task CreateSession_KnownAgent_ReturnsSession()
    {
        var definitions = new Dictionary<string, AgentDefinition>
        {
            ["test-agent"] = new AgentDefinition
            {
                Name = "test-agent",
                Model = "sonnet",
                Provider = "anthropic",
                SystemPrompt = "You are a test agent"
            }
        };

        var orchestrator = new AfAgentOrchestrator(
            CreateFactory(),
            CreatePassthroughPipeline(),
            new AgentDefinitionRegistry(definitions),
            NullLoggerFactory.Instance);

        var session = await orchestrator.CreateSessionAsync("test-agent");

        session.Should().NotBeNull();
        session.AgentName.Should().Be("test-agent");
        session.SessionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateSession_UnknownAgent_Throws()
    {
        var orchestrator = new AfAgentOrchestrator(
            CreateFactory(),
            CreatePassthroughPipeline(),
            new AgentDefinitionRegistry(),
            NullLoggerFactory.Instance);

        var act = () => orchestrator.CreateSessionAsync("nonexistent");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ListAgents_ReturnsConfigured()
    {
        var definitions = new Dictionary<string, AgentDefinition>
        {
            ["agent-a"] = new() { Name = "agent-a", Model = "sonnet" },
            ["agent-b"] = new() { Name = "agent-b", Model = "haiku" }
        };

        var orchestrator = new AfAgentOrchestrator(
            CreateFactory(),
            CreatePassthroughPipeline(),
            new AgentDefinitionRegistry(definitions),
            NullLoggerFactory.Instance);

        var agents = await orchestrator.ListAgentsAsync();
        agents.Should().HaveCount(2);
        agents.Should().Contain("agent-a");
        agents.Should().Contain("agent-b");
    }
}
