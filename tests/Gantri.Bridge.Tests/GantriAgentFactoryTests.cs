using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Mcp;
using Gantri.Abstractions.Plugins;
using Gantri.AI;
using Gantri.Bridge;
using Gantri.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gantri.Bridge.Tests;

public class GantriAgentFactoryTests
{
    private static IHookPipeline CreatePassthroughPipeline()
    {
        var pipeline = Substitute.For<IHookPipeline>();
        pipeline
            .ExecuteAsync(
                Arg.Any<HookEvent>(),
                Arg.Any<Func<HookContext, ValueTask>>(),
                Arg.Any<HookContext?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
            {
                var ctx = new HookContext(callInfo.ArgAt<HookEvent>(0));
                return new ValueTask<HookContext>(ctx);
            });
        return pipeline;
    }

    [Fact]
    public async Task CreateAgentAsync_CollectsPluginTools()
    {
        var registry = new ModelProviderRegistry();
        registry.RegisterProvider(
            "test-provider",
            new AiProviderOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://test.openai.azure.com",
                Models = new Dictionary<string, AiModelOptions>
                {
                    ["test-model"] = new AiModelOptions { Id = "gpt-4o" },
                },
            }
        );

        var pluginManifest = new PluginManifest
        {
            Name = "file-save",
            Version = "1.0.0",
            Description = "File save plugin",
            Exports = new PluginExports
            {
                Actions = [new PluginActionExport { Name = "save", Description = "Save a file" }],
            },
        };

        var plugin = Substitute.For<IPlugin>();
        plugin.Manifest.Returns(pluginManifest);

        var pluginRouter = Substitute.For<IPluginRouter>();
        pluginRouter.ResolveAsync("file-save", Arg.Any<CancellationToken>()).Returns(plugin);

        var mcpToolProvider = Substitute.For<IMcpToolProvider>();
        mcpToolProvider
            .GetToolsAsync("brave", Arg.Any<CancellationToken>())
            .Returns(
                new List<McpToolInfo>
                {
                    new()
                    {
                        ServerName = "brave",
                        ToolName = "search",
                        Description = "Search the web",
                    },
                }
            );

        var mockClient = Substitute.For<IChatClient>();
        Func<string, AiModelOptions, IChatClient> clientFactory = (_, _) => mockClient;

        var factory = new GantriAgentFactory(
            registry,
            pluginRouter,
            mcpToolProvider,
            CreatePassthroughPipeline(),
            NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()),
            clientFactory
        );

        var definition = new AgentDefinition
        {
            Name = "test-agent",
            Model = "test-model",
            Provider = "test-provider",
            Plugins = ["file-save"],
            McpServers = ["brave"],
        };

        var agent = await factory.CreateAgentAsync(definition);

        agent.Should().NotBeNull();
        agent.Name.Should().Be("test-agent");

        // Verify plugins and MCP tools were queried
        await pluginRouter.Received(1).ResolveAsync("file-save", Arg.Any<CancellationToken>());
        await mcpToolProvider.Received(1).GetToolsAsync("brave", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAgentAsync_NoTools_CreatesAgentWithoutTools()
    {
        var registry = new ModelProviderRegistry();
        registry.RegisterProvider(
            "provider",
            new AiProviderOptions
            {
                ApiKey = "key",
                Endpoint = "https://test.openai.azure.com",
                Models = new Dictionary<string, AiModelOptions>
                {
                    ["model"] = new AiModelOptions { Id = "gpt-4o" },
                },
            }
        );

        var mockClient = Substitute.For<IChatClient>();
        Func<string, AiModelOptions, IChatClient> clientFactory = (_, _) => mockClient;

        var factory = new GantriAgentFactory(
            registry,
            Substitute.For<IPluginRouter>(),
            Substitute.For<IMcpToolProvider>(),
            CreatePassthroughPipeline(),
            NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()),
            clientFactory
        );

        var definition = new AgentDefinition
        {
            Name = "simple-agent",
            Model = "model",
            Provider = "provider",
            SystemPrompt = "You are a test agent.",
        };

        var agent = await factory.CreateAgentAsync(definition);

        agent.Should().NotBeNull();
        agent.Name.Should().Be("simple-agent");
    }

    [Fact]
    public async Task CreateAgentAsync_WithApprovalHandler_ThreadsThroughToTools()
    {
        var registry = new ModelProviderRegistry();
        registry.RegisterProvider(
            "test-provider",
            new AiProviderOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://test.openai.azure.com",
                Models = new Dictionary<string, AiModelOptions>
                {
                    ["test-model"] = new AiModelOptions { Id = "gpt-4o" },
                },
            }
        );

        var pluginManifest = new PluginManifest
        {
            Name = "test-plugin",
            Version = "1.0.0",
            Description = "Test",
            Exports = new PluginExports
            {
                Actions = [new PluginActionExport { Name = "action", Description = "Test action" }],
            },
        };

        var plugin = Substitute.For<IPlugin>();
        plugin.Manifest.Returns(pluginManifest);

        var pluginRouter = Substitute.For<IPluginRouter>();
        pluginRouter.ResolveAsync("test-plugin", Arg.Any<CancellationToken>()).Returns(plugin);

        var mcpToolProvider = Substitute.For<IMcpToolProvider>();
        var approvalHandler = Substitute.For<IToolApprovalHandler>();
        var mockClient = Substitute.For<IChatClient>();
        Func<string, AiModelOptions, IChatClient> clientFactory = (_, _) => mockClient;

        var factory = new GantriAgentFactory(
            registry,
            pluginRouter,
            mcpToolProvider,
            CreatePassthroughPipeline(),
            NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()),
            clientFactory,
            approvalHandler
        );

        var definition = new AgentDefinition
        {
            Name = "test-agent",
            Model = "test-model",
            Provider = "test-provider",
            Plugins = ["test-plugin"],
        };

        var agent = await factory.CreateAgentAsync(definition);

        agent.Should().NotBeNull();
        // Verify tools were created (the approval handler is passed to tool constructors)
        await pluginRouter.Received(1).ResolveAsync("test-plugin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAgentAsync_WithMcpPermissions_SkipsUnauthorizedServers()
    {
        var registry = new ModelProviderRegistry();
        registry.RegisterProvider(
            "test-provider",
            new AiProviderOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://test.openai.azure.com",
                Models = new Dictionary<string, AiModelOptions>
                {
                    ["test-model"] = new AiModelOptions { Id = "gpt-4o" },
                },
            }
        );

        var pluginRouter = Substitute.For<IPluginRouter>();
        var mcpToolProvider = Substitute.For<IMcpToolProvider>();
        var permissionManager = new McpPermissionManager();
        permissionManager.AddAgentServer("test-agent", "github");

        var mockClient = Substitute.For<IChatClient>();
        Func<string, AiModelOptions, IChatClient> clientFactory = (_, _) => mockClient;

        var factory = new GantriAgentFactory(
            registry,
            pluginRouter,
            mcpToolProvider,
            CreatePassthroughPipeline(),
            NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()),
            clientFactory,
            mcpPermissionManager: permissionManager
        );

        var definition = new AgentDefinition
        {
            Name = "test-agent",
            Model = "test-model",
            Provider = "test-provider",
            McpServers = ["brave"],
        };

        var agent = await factory.CreateAgentAsync(definition);

        agent.Should().NotBeNull();
        await mcpToolProvider.DidNotReceive().GetToolsAsync("brave", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAgentAsync_WithAllowedActions_SkipsPluginWithoutAllowedPrefix()
    {
        var registry = new ModelProviderRegistry();
        registry.RegisterProvider(
            "test-provider",
            new AiProviderOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://test.openai.azure.com",
                Models = new Dictionary<string, AiModelOptions>
                {
                    ["test-model"] = new AiModelOptions { Id = "gpt-4o" },
                },
            }
        );

        var pluginRouter = Substitute.For<IPluginRouter>();
        var mcpToolProvider = Substitute.For<IMcpToolProvider>();
        var mockClient = Substitute.For<IChatClient>();
        Func<string, AiModelOptions, IChatClient> clientFactory = (_, _) => mockClient;

        var factory = new GantriAgentFactory(
            registry,
            pluginRouter,
            mcpToolProvider,
            CreatePassthroughPipeline(),
            NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()),
            clientFactory
        );

        var definition = new AgentDefinition
        {
            Name = "test-agent",
            Model = "test-model",
            Provider = "test-provider",
            Plugins = ["file-save"],
            AllowedActions = ["other.action"],
        };

        var agent = await factory.CreateAgentAsync(definition);

        agent.Should().NotBeNull();
        await pluginRouter.DidNotReceive().ResolveAsync("file-save", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAgentAsync_WithAllowedActionsWildcard_LoadsMatchingMcpServer()
    {
        var registry = new ModelProviderRegistry();
        registry.RegisterProvider(
            "test-provider",
            new AiProviderOptions
            {
                ApiKey = "test-key",
                Endpoint = "https://test.openai.azure.com",
                Models = new Dictionary<string, AiModelOptions>
                {
                    ["test-model"] = new AiModelOptions { Id = "gpt-4o" },
                },
            }
        );

        var pluginRouter = Substitute.For<IPluginRouter>();
        var mcpToolProvider = Substitute.For<IMcpToolProvider>();
        mcpToolProvider
            .GetToolsAsync("brave", Arg.Any<CancellationToken>())
            .Returns(
                new List<McpToolInfo>
                {
                    new()
                    {
                        ServerName = "brave",
                        ToolName = "search",
                        Description = "Search",
                    },
                }
            );

        var mockClient = Substitute.For<IChatClient>();
        Func<string, AiModelOptions, IChatClient> clientFactory = (_, _) => mockClient;

        var factory = new GantriAgentFactory(
            registry,
            pluginRouter,
            mcpToolProvider,
            CreatePassthroughPipeline(),
            NullLogger<GantriAgentFactory>.Instance,
            NullLoggerFactory.Instance,
            Options.Create(new WorkingDirectoryOptions()),
            clientFactory
        );

        var definition = new AgentDefinition
        {
            Name = "test-agent",
            Model = "test-model",
            Provider = "test-provider",
            McpServers = ["brave"],
            AllowedActions = ["brave.*"],
        };

        var agent = await factory.CreateAgentAsync(definition);

        agent.Should().NotBeNull();
        await mcpToolProvider.Received(1).GetToolsAsync("brave", Arg.Any<CancellationToken>());
    }
}
