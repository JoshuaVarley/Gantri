using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Agents;
using Gantri.AI;
using Gantri.Bridge;
using Gantri.Configuration;
using Gantri.Hooks;
using Gantri.Mcp;
using Gantri.Plugins;
using Gantri.Plugins.Wasm;
using Gantri.Telemetry;
using Gantri.Workflows;
using A2A;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// Resolve config path and project root.
// WebApplication.CreateBuilder sets CWD to the project directory, not the repo root.
var (configPath, projectRoot) = ResolveConfigPath();
Directory.SetCurrentDirectory(projectRoot);

// Layer additional secret sources into the host configuration.
builder.Configuration
    .AddDotEnvFile(optional: true)
    .AddUserSecrets<Program>(optional: true);

ISecretResolver secretResolver = new ConfigurationSecretResolver(builder.Configuration);

// Core Gantri services
builder.Services.AddHttpClient().AddLogging();
builder.Services.AddGantriHooks();
builder.Services.AddGantriPlugins();
builder.Services.AddGantriWasmPlugins();
builder.Services.AddGantriAI();
builder.Services.AddGantriMcp();
builder.Services.AddGantriConfiguration(configPath);

// Domain
builder.Services.AddGantriAgents();
builder.Services.AddGantriWorkflows();

// Integration — Bridge wires AF engines over domain defaults and registers IAgentProvider
builder.Services.AddGantriBridge();

// Register ISecretResolver for DI consumers
builder.Services.AddSingleton<ISecretResolver>(secretResolver);

// Web host auto-approves tool calls (no interactive console)
builder.Services.AddSingleton<IToolApprovalHandler>(new AutoApproveToolHandler());

// Load config eagerly for registration
GantriConfigRoot? config = null;
if (configPath is not null)
{
    var loader = new YamlConfigurationLoader(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<YamlConfigurationLoader>.Instance,
        secretResolver
    );
    config = loader.LoadTypedWithImports<GantriConfigRoot>(configPath);
}

// Validate config at startup
if (config is not null)
{
    var validator = new ConfigValidator(
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger<ConfigValidator>()
    );
    var errors = validator.Validate(config);
    if (errors.Count > 0)
    {
        foreach (var error in errors)
            Console.Error.WriteLine($"Config error: {error}");
    }
}

// Telemetry
builder.Services.AddGantriTelemetry(config?.Telemetry);

if (TelemetryServiceExtensions.UsesOtlpLogExporter(config?.Telemetry))
    builder.Logging.ClearProviders();

// Default working directory for agent file confinement
builder.Services.Configure<WorkingDirectoryOptions>(o =>
    o.DefaultDirectory = Path.GetFullPath(config?.Framework.DataDir ?? "./data"));

// Shared host registrations: definition registries, scheduling jobs
builder.Services.AddGantriFromConfig(config);

// Chat client factory for GantriAgentFactory
if (config?.Ai.Providers is { Count: > 0 })
{
    var providers = config.Ai.Providers;
    builder.Services.AddSingleton<Func<string, AiModelOptions, IChatClient>>(
        (providerName, model) =>
        {
            if (!providers.TryGetValue(providerName, out var providerOpts))
                throw new InvalidOperationException($"Provider '{providerName}' not found.");

            return ChatClientFactory.Create(providerName, providerOpts, model);
        }
    );
}

// AG-UI services
builder.Services.AddAGUI();

var app = builder.Build();

// Post-build initialization: populate model provider registry
if (config?.Ai.Providers is { Count: > 0 })
{
    var registry = app.Services.GetRequiredService<ModelProviderRegistry>();
    foreach (var (name, opts) in config.Ai.Providers)
        registry.RegisterProvider(name, opts);
}

// Resolve relative plugin paths against the config file's directory
if (config?.Plugins.Dirs is { Count: > 0 } && configPath is not null)
{
    var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";
    var resolvedDirs = config.Plugins.Dirs
        .Select(dir => Path.IsPathRooted(dir) ? dir : Path.GetFullPath(Path.Combine(configDir, dir)))
        .ToList();

    var pluginRouter = app.Services.GetRequiredService<PluginRouter>();
    pluginRouter.ScanPluginDirectories(resolvedDirs);
}

// Register configured MCP servers
var mcpManager = app.Services.GetRequiredService<McpClientManager>();
mcpManager.RegisterMcpServers(config?.Mcp, secretResolver);
var mcpPermissionManager = app.Services.GetRequiredService<McpPermissionManager>();
mcpPermissionManager.RegisterMcpPermissions(config?.Agents);

// Health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Map AG-UI and A2A endpoints for each configured agent.
// IAgentProvider creates AIAgent instances with all Gantri concerns (plugins, hooks, security, resilience).
var agentProvider = app.Services.GetRequiredService<IAgentProvider>();
var definitionRegistry = app.Services.GetRequiredService<IAgentDefinitionRegistry>();

foreach (var agentName in agentProvider.AgentNames)
{
    var agent = await agentProvider.GetAgentAsync(agentName);
    var definition = definitionRegistry.TryGet(agentName);

    // AG-UI: SSE streaming for web frontends (CopilotKit, custom UIs)
    app.MapAGUI($"/agents/{agentName}", agent);

    // A2A: agent-to-agent communication with discovery via AgentCards
    var description = definition?.SystemPrompt?[..Math.Min(200, definition.SystemPrompt?.Length ?? 0)]
        ?? $"Gantri agent: {agentName}";
    app.MapA2A(agent, $"/a2a/{agentName}", agentCard: new AgentCard
    {
        Name = agentName,
        Description = description,
        Version = "1.0"
    });
}

// Startup banner
Console.Error.WriteLine($"Gantri Web started — {agentProvider.AgentNames.Count} agent(s) available");
foreach (var name in agentProvider.AgentNames)
{
    Console.Error.WriteLine($"  AG-UI: /agents/{name}");
    Console.Error.WriteLine($"  A2A:   /a2a/{name}");
}

app.Run();

static (string? ConfigPath, string ProjectRoot) ResolveConfigPath()
{
    string[] names = ["config/gantri.yaml", "config/gantri.yml", "gantri.yaml", "gantri.yml"];

    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        foreach (var name in names)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (File.Exists(candidate))
                return (candidate, dir.FullName);
        }
        dir = dir.Parent;
    }

    return (null, Directory.GetCurrentDirectory());
}
