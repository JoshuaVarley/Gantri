using Gantri.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gantri.Configuration;

public static class GantriHostExtensions
{
    /// <summary>
    /// Registers post-config Gantri services shared between CLI and Worker hosts.
    /// Call after AddGantriConfiguration, AddGantriAgents, AddGantriWorkflows, AddGantriBridge.
    /// </summary>
    public static IServiceCollection AddGantriFromConfig(this IServiceCollection services, GantriConfigRoot? config)
    {
        // Register typed definition registries (always, even if empty)
        services.AddSingleton<IAgentDefinitionRegistry>(
            new AgentDefinitionRegistry(config?.Agents));
        services.AddSingleton<IWorkflowDefinitionRegistry>(
            new WorkflowDefinitionRegistry(config?.Workflows));

        // Register scheduling job definitions when present
        if (config?.Scheduling.Jobs is { Count: > 0 })
            services.AddSingleton(config.Scheduling.Jobs);

        return services;
    }
}
