using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Scheduling;
using Gantri.Agents;
using Gantri.AI;
using Gantri.Bridge;
using Gantri.Cli.Commands;
using Gantri.Cli.Infrastructure;
using Gantri.Cli.Interactive;
using Gantri.Cli.Interactive.Commands;
using Gantri.Configuration;
using Gantri.Hooks;
using Gantri.Mcp;
using Gantri.Plugins;
using Gantri.Plugins.Wasm;
using Gantri.Scheduling;
using Gantri.Telemetry;
using Gantri.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

// Resolve config path — probe known locations
var configPath = ResolveConfigPath();

// Build layered configuration (last wins = highest priority)
var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{env}.json", optional: true)
    .AddDotEnvFile(optional: true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>(optional: true)
    .Build();

ISecretResolver secretResolver = new ConfigurationSecretResolver(configuration);

// Build service collection with all Gantri services
var services = new ServiceCollection();

// Default working directory for agent file confinement
services.Configure<WorkingDirectoryOptions>(o =>
    o.DefaultDirectory = Path.GetFullPath(Environment.CurrentDirectory));

// Core infrastructure — register logging early so DI can resolve ILogger during config load.
// The console provider is added conditionally after config is loaded (see below).
services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information));
services.AddGantriHooks();
services.AddGantriPlugins();
services.AddGantriWasmPlugins();
services.AddGantriAI();
services.AddGantriMcp();
services.AddGantriConfiguration(configPath);

// Domain
services.AddGantriAgents();
services.AddGantriWorkflows();

// Integration — Bridge wires AF engines over domain defaults
services.AddGantriBridge();

// Register ISecretResolver for DI consumers
services.AddSingleton<ISecretResolver>(secretResolver);

// Load config eagerly so we can extract definitions and register the AI factory
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
services.AddGantriTelemetry(config?.Telemetry);

// When logs are NOT routed through OTLP, add the console log provider for local output.
if (!TelemetryServiceExtensions.UsesOtlpLogExporter(config?.Telemetry))
    services.AddLogging(builder => builder.AddConsole());

// Scheduling — CLI uses a lightweight read-only scheduler (no TickerQ runtime)
var jobDefs = config?.Scheduling.Jobs ?? new Dictionary<string, ScheduledJobDefinition>();
services.AddSingleton<IJobScheduler>(new ReadOnlyJobScheduler(jobDefs));

// Shared host registrations: definition registries, scheduling jobs
services.AddGantriFromConfig(config);

// Register Azure-aware chat client factory for GantriAgentFactory
if (config?.Ai.Providers is { Count: > 0 })
{
    var providers = config.Ai.Providers;

    services.AddSingleton<Func<string, AiModelOptions, IChatClient>>(sp =>
        (providerName, model) =>
        {
            if (!providers.TryGetValue(providerName, out var providerOpts))
                throw new InvalidOperationException(
                    $"Provider '{providerName}' not found in configuration."
                );

            return ChatClientFactory.Create(providerName, providerOpts, model);
        }
    );
}

// Worker MCP client (needs WorkerOptions from config)
services.AddSingleton(config?.Worker ?? new WorkerOptions());
services.AddTransient<WorkerMcpClient>();

// Register command types for Spectre.Console DI resolution
services.AddTransient<AgentRunCommand>();
services.AddTransient<AgentListCommand>();
services.AddTransient<PluginListCommand>();
services.AddTransient<PluginInspectCommand>();
services.AddTransient<ConfigShowCommand>();
services.AddTransient<ConfigValidateCommand>();
services.AddTransient<ConfigInitCommand>();
services.AddTransient<WorkflowRunCommand>();
services.AddTransient<WorkflowListCommand>();
services.AddTransient<WorkflowStatusCommand>();
services.AddTransient<ScheduleListCommand>();
services.AddTransient<WorkerStatusCommand>();
services.AddTransient<WorkerJobsListCommand>();
services.AddTransient<WorkerJobsTriggerCommand>();
services.AddTransient<GroupChatCommand>();

// Interactive console services
var isInteractive = args.Length == 0;

services.AddSingleton<ConsoleRenderer>();
services.AddSingleton<SlashCommandRouter>(sp =>
{
    var router = new SlashCommandRouter();
    router.Register(new AgentCommand());
    router.Register(new WorkflowCommand());
    router.Register(new GroupChatInteractiveCommand());
    router.Register(new ApproveCommand());
    router.Register(new HelpCommand(router));
    router.Register(new ToolsCommand());
    router.Register(new SessionCommand());
    router.Register(new ClearCommand());
    router.Register(new ExitCommand());
    router.Register(new ScheduleCommand());
    router.Register(new QuitCommand());
    return router;
});
services.AddTransient<InteractiveConsole>();

if (isInteractive)
{
    // Interactive mode: register interactive tool approval
    services.AddSingleton<IToolApprovalHandler>(sp => new InteractiveToolApprovalHandler(
        sp.GetRequiredService<ConsoleRenderer>()
    ));

    // Replace the default ApprovalStepHandler with the interactive version
    var existing = services.FirstOrDefault(d =>
        d.ServiceType == typeof(IStepHandler)
        && d.ImplementationType == typeof(Gantri.Workflows.Steps.ApprovalStepHandler)
    );
    if (existing is not null)
        services.Remove(existing);

    services.AddSingleton<IStepHandler>(sp => new InteractiveApprovalStepHandler(
        sp.GetRequiredService<ConsoleRenderer>()
    ));
}

await using var serviceProvider = services.BuildServiceProvider();

// Force OTel trace/metric providers to initialize — the CLI doesn't use IHost,
// so the OTel hosted service that normally does this never runs.
TelemetryServiceExtensions.EnsureProvidersInitialized(serviceProvider);

// Populate ModelProviderRegistry from config
if (config?.Ai.Providers is { Count: > 0 })
{
    var registry = serviceProvider.GetRequiredService<ModelProviderRegistry>();
    foreach (var (name, opts) in config.Ai.Providers)
    {
        registry.RegisterProvider(name, opts);
    }
}

// Initialize plugin discovery from config directories.
// Resolve relative paths against the config file's directory so plugins are found
// regardless of the process's current working directory.
if (config?.Plugins.Dirs is { Count: > 0 } && configPath is not null)
{
    var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";
    var resolvedDirs = config.Plugins.Dirs
        .Select(dir => Path.IsPathRooted(dir) ? dir : Path.GetFullPath(Path.Combine(configDir, dir)))
        .ToList();

    var pluginRouter = serviceProvider.GetRequiredService<PluginRouter>();
    pluginRouter.ScanPluginDirectories(resolvedDirs);
}

// Register configured MCP servers
var mcpManager = serviceProvider.GetRequiredService<McpClientManager>();
mcpManager.RegisterMcpServers(config?.Mcp, secretResolver);
var mcpPermissionManager = serviceProvider.GetRequiredService<McpPermissionManager>();
mcpPermissionManager.RegisterMcpPermissions(config?.Agents);

// Configure Spectre.Console command tree
var app = new CommandApp(new GantriRegistrar(serviceProvider));

app.Configure(appConfig =>
{
    appConfig.SetApplicationName("gantri");
    appConfig.SetApplicationVersion("0.1.0");

    appConfig.AddBranch(
        "agent",
        agent =>
        {
            agent.SetDescription("Manage and run agents");
            agent
                .AddCommand<AgentRunCommand>("run")
                .WithDescription("Run an agent interactively or with a single input");
            agent
                .AddCommand<AgentListCommand>("list")
                .WithDescription("List all configured agents");
        }
    );

    appConfig.AddBranch(
        "workflow",
        workflow =>
        {
            workflow.SetDescription("Manage and run workflows");
            workflow.AddCommand<WorkflowRunCommand>("run").WithDescription("Run a workflow");
            workflow
                .AddCommand<WorkflowListCommand>("list")
                .WithDescription("List all configured workflows");
            workflow
                .AddCommand<WorkflowStatusCommand>("status")
                .WithDescription("Show status of a workflow execution");
        }
    );

    appConfig.AddBranch(
        "orchestrate",
        orchestrate =>
        {
            orchestrate.SetDescription("Multi-agent orchestration");
            orchestrate
                .AddCommand<GroupChatCommand>("group-chat")
                .WithDescription("Run a group chat with multiple agents");
        }
    );

    appConfig.AddBranch(
        "schedule",
        schedule =>
        {
            schedule.SetDescription("Manage scheduled jobs");
            schedule
                .AddCommand<ScheduleListCommand>("list")
                .WithDescription("List all scheduled jobs");
        }
    );

    appConfig.AddBranch(
        "plugin",
        plugin =>
        {
            plugin.SetDescription("Manage plugins");
            plugin
                .AddCommand<PluginListCommand>("list")
                .WithDescription("List all discovered plugins");
            plugin
                .AddCommand<PluginInspectCommand>("inspect")
                .WithDescription("Show detailed information about a plugin");
        }
    );

    appConfig.AddBranch(
        "config",
        cfg =>
        {
            cfg.SetDescription("Configuration management");
            cfg.AddCommand<ConfigShowCommand>("show").WithDescription("Show current configuration");
            cfg.AddCommand<ConfigValidateCommand>("validate")
                .WithDescription("Validate configuration file");
            cfg.AddCommand<ConfigInitCommand>("init")
                .WithDescription("Scaffold a split config directory");
        }
    );

    appConfig.AddBranch(
        "worker",
        worker =>
        {
            worker.SetDescription("Manage the Gantri worker");
            worker
                .AddCommand<WorkerStatusCommand>("status")
                .WithDescription("Show worker health status");
            worker.AddBranch(
                "jobs",
                jobs =>
                {
                    jobs.SetDescription("Manage worker scheduled jobs");
                    jobs.AddCommand<WorkerJobsListCommand>("list")
                        .WithDescription("List all scheduled jobs from the worker");
                    jobs.AddCommand<WorkerJobsTriggerCommand>("trigger")
                        .WithDescription("Manually trigger a scheduled job");
                }
            );
        }
    );
});

// Dual-mode entry: interactive console when no args, otherwise CLI subcommands
if (isInteractive)
{
    var console = serviceProvider.GetRequiredService<InteractiveConsole>();
    return await console.RunAsync();
}

return await app.RunAsync(args);

static string? ResolveConfigPath()
{
    string[] candidates =
    [
        Path.Combine("config", "gantri.yaml"),
        Path.Combine("config", "gantri.yml"),
        "gantri.yaml",
        "gantri.yml",
    ];

    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
            return candidate;
    }

    return null;
}
