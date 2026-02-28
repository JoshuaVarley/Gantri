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
    /// Registers the Gantri Bridge services: <see cref="GantriAgentFactory"/>,
    /// <see cref="AfAgentOrchestrator"/> (as <see cref="IAgentOrchestrator"/>),
    /// <see cref="AfWorkflowEngine"/> (overrides <see cref="IWorkflowEngine"/>),
    /// and <see cref="InvokeGantriPluginAction"/>.
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
            var clientFactory = sp.GetService<Func<string, AiModelOptions, IChatClient>>();
            var approvalHandler = sp.GetService<IToolApprovalHandler>();
            var mcpPermissionManager = sp.GetService<McpPermissionManager>();

            return new GantriAgentFactory(
                registry,
                pluginRouter,
                mcpToolProvider,
                hookPipeline,
                logger,
                loggerFactory,
                workingDirectoryOptions,
                clientFactory,
                approvalHandler,
                mcpPermissionManager
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

        // InvokeGantriPluginAction for declarative AF workflows
        services.AddSingleton<InvokeGantriPluginAction>();

        return services;
    }
}
