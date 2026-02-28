using Gantri.Abstractions.Configuration;
using Gantri.Agents;
using Gantri.AI;
using Gantri.Bridge;
using Gantri.Configuration;
using Gantri.Hooks;
using Gantri.Mcp;
using Gantri.Plugins;
using Gantri.Plugins.Wasm;
using Gantri.Scheduling;
using Gantri.Telemetry;
using Gantri.Worker;
using Gantri.Workflows;
using Microsoft.Extensions.AI;
using TickerQ.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Resolve config path and project root.
// WebApplication.CreateBuilder sets CWD to the project directory, not the repo root.
// Derive the project root from the config file location so relative paths in config
// (working_directory, data_dir, plugin dirs) resolve correctly.
var (configPath, projectRoot) = ResolveConfigPath();
Directory.SetCurrentDirectory(projectRoot);

// Layer additional secret sources into the host configuration.
// WebApplicationBuilder already adds appsettings.json + environment variables.
builder.Configuration
    .AddDotEnvFile(optional: true)
    .AddUserSecrets<Program>(optional: true);

ISecretResolver secretResolver = new ConfigurationSecretResolver(builder.Configuration);

// Core Gantri services
builder.Services.AddGantriHooks();
builder.Services.AddGantriPlugins();
builder.Services.AddGantriWasmPlugins();
builder.Services.AddGantriAI();
builder.Services.AddGantriMcp();
builder.Services.AddGantriConfiguration(configPath);

// Domain
builder.Services.AddGantriAgents();
builder.Services.AddGantriWorkflows();

// Integration — Bridge wires AF engines over domain defaults
builder.Services.AddGantriBridge();

// Register ISecretResolver for DI consumers
builder.Services.AddSingleton<ISecretResolver>(secretResolver);

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

// Telemetry — configured after config is loaded so TelemetryOptions are available.
// OTLP exporter respects OTEL_EXPORTER_OTLP_ENDPOINT / OTEL_EXPORTER_OTLP_PROTOCOL env vars.
builder.Services.AddGantriTelemetry(config?.Telemetry);

// When logs are routed through OTLP, remove the default console log provider
// so the terminal stays clean.
if (TelemetryServiceExtensions.UsesOtlpLogExporter(config?.Telemetry))
    builder.Logging.ClearProviders();

// Default working directory for agent file confinement
builder.Services.Configure<WorkingDirectoryOptions>(o =>
    o.DefaultDirectory = Path.GetFullPath(config?.Framework.DataDir ?? "./data"));

// Shared host registrations: definition registries, scheduling jobs
builder.Services.AddGantriFromConfig(config);

// Register config-derived services
if (config is not null)
{
    // Scheduling with TickerQ (uses config storage options)
    builder.Services.AddGantriScheduling(config.Scheduling.Storage);

    // Chat client factory for GantriAgentFactory
    if (config.Ai.Providers.Count > 0)
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
}
else
{
    // No config: register scheduling with defaults
    builder.Services.AddGantriScheduling();
}

// Worker health and MCP server
builder.Services.AddSingleton<WorkerHealthService>();
builder.Services.AddSingleton<WorkerMcpServer>();

// Only register the stdio MCP server transport when explicitly requested via --mcp flag.
// Stdio transport reads from stdin — if launched standalone, this blocks the app indefinitely.
// Note: Console.IsInputRedirected is unreliable on MSYS/Git Bash, so we use an explicit flag.
if (args.Contains("--mcp"))
{
    builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
}

// Hosted service for scheduler lifecycle
builder.Services.AddHostedService<SchedulerHostedService>();

var app = builder.Build();

// Ensure TickerQ database tables exist
using (var scope = app.Services.CreateScope())
{
    var dbContext =
        scope.ServiceProvider.GetRequiredService<TickerQ.EntityFrameworkCore.DbContextFactory.TickerQDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

// Activate TickerQ job processor
app.UseTickerQ();

// Health endpoint for container orchestrators (Kubernetes, Docker)
app.MapGet("/health", (WorkerHealthService health) => Results.Ok(health.GetStatus()));

// Post-build initialization
if (config?.Ai.Providers is { Count: > 0 })
{
    var registry = app.Services.GetRequiredService<ModelProviderRegistry>();
    foreach (var (name, opts) in config.Ai.Providers)
        registry.RegisterProvider(name, opts);
}

// Resolve relative plugin paths against the config file's directory so plugins are found
// regardless of the process's current working directory.
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

// Startup banner — written to stderr directly so it's visible even when console logging
// is disabled in favour of OTLP.
var jobCount = config?.Scheduling.Jobs.Count ?? 0;
Console.Error.WriteLine($"Gantri Worker started — {jobCount} scheduled job(s)");

app.Run();

static (string? ConfigPath, string ProjectRoot) ResolveConfigPath()
{
    // WebApplication.CreateBuilder sets CWD to the project directory, not the repo root.
    // Walk up from CWD to find config files in the expected locations.
    // Returns both the config path and the directory it was found in (the project root).
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
