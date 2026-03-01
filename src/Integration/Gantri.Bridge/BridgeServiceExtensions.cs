using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Mcp;
using Gantri.Abstractions.Plugins;
using Gantri.Abstractions.Workflows;
using Gantri.AI;
using Gantri.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gantri.Bridge;

public static class BridgeServiceExtensions
{
    /// <summary>
    /// Registers the Gantri Bridge services:
    /// <list type="bullet">
    /// <item><see cref="GantriAgentFactory"/> — builds AF AIAgent from YAML config with plugins, hooks, security</item>
    /// <item><see cref="IAgentProvider"/> — exposes raw AIAgent instances for AG-UI/A2A protocol hosts</item>
    /// <item><see cref="IAgentOrchestrator"/> — string-based agent interaction for CLI/Worker hosts</item>
    /// <item><see cref="AfWorkflowEngine"/> — AF-aware workflow routing (overrides domain IWorkflowEngine)</item>
    /// <item><see cref="InvokeGantriPluginAction"/> — plugin action type for AF declarative workflows</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddGantriBridge(this IServiceCollection services)
    {
        services.AddSingleton<GantriAgentFactory>(sp =>
        {
            var registry = sp.GetRequiredService<ModelProviderRegistry>();
            var pluginRouter = sp.GetRequiredService<IPluginRouter>();
            var mcpToolProvider = sp.GetRequiredService<IMcpToolProvider>();
            var hookPipeline = sp.GetRequiredService<IHookPipeline>();
            var logger = sp.GetRequiredService<ILogger<GantriAgentFactory>>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var workingDirectoryOptions = sp.GetRequiredService<IOptions<WorkingDirectoryOptions>>();
            var telemetryOptions = sp.GetRequiredService<IOptions<TelemetryOptions>>();
            var clientFactory = sp.GetService<Func<string, AiModelOptions, IChatClient>>();
            var approvalHandler = sp.GetService<IToolApprovalHandler>();
            var mcpPermissionManager = sp.GetService<McpPermissionManager>();
            var pluginServices = sp.GetService<IPluginServices>();

            return new GantriAgentFactory(
                registry,
                pluginRouter,
                mcpToolProvider,
                hookPipeline,
                logger,
                loggerFactory,
                workingDirectoryOptions,
                telemetryOptions,
                clientFactory,
                approvalHandler,
                mcpPermissionManager,
                pluginServices
            );
        });

        services.AddSingleton<AfAgentOrchestrator>(sp =>
        {
            return new AfAgentOrchestrator(
                sp.GetRequiredService<GantriAgentFactory>(),
                sp.GetRequiredService<IHookPipeline>(),
                sp.GetRequiredService<IAgentDefinitionRegistry>(),
                sp.GetRequiredService<ILoggerFactory>()
            );
        });
        services.AddSingleton<IAgentOrchestrator>(sp =>
            sp.GetRequiredService<AfAgentOrchestrator>()
        );

        // AF Workflow Engine wraps legacy engine with AF routing for simple sequential workflows.
        // Overrides the domain IWorkflowEngine registration.
        services.AddSingleton<AfWorkflowEngine>(sp =>
        {
            return new AfWorkflowEngine(
                sp.GetRequiredService<ILegacyWorkflowEngine>(),
                sp.GetRequiredService<GantriAgentFactory>(),
                sp.GetRequiredService<IWorkflowDefinitionRegistry>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<AfWorkflowEngine>(),
                sp.GetRequiredService<IAgentDefinitionRegistry>()
            );
        });

        // Override IWorkflowEngine to point at the AF wrapper
        services.AddSingleton<IWorkflowEngine>(sp => sp.GetRequiredService<AfWorkflowEngine>());

        // IAgentProvider exposes raw AIAgent instances for AG-UI/A2A hosts
        services.AddSingleton<GantriAgentProvider>();
        services.AddSingleton<IAgentProvider>(sp => sp.GetRequiredService<GantriAgentProvider>());

        // InvokeGantriPluginAction for declarative AF workflows
        services.AddSingleton<InvokeGantriPluginAction>();

        return services;
    }
}
