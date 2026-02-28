using Gantri.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gantri.Configuration;

public static class GantriConfigurationExtensions
{
    public static IServiceCollection AddGantriConfiguration(this IServiceCollection services, string? configPath = null)
    {
        services.AddSingleton<YamlConfigurationLoader>();
        services.AddSingleton<ConfigValidator>();

        if (configPath is not null)
        {
            services.AddSingleton(sp =>
            {
                var loader = sp.GetRequiredService<YamlConfigurationLoader>();
                return loader.LoadTypedWithImports<GantriConfigRoot>(configPath);
            });
        }

        return services;
    }
}

public sealed class GantriConfigRoot
{
    public GantriOptions Framework { get; set; } = new();
    public AiOptions Ai { get; set; } = new();
    public PluginOptions Plugins { get; set; } = new();
    public HookOptions Hooks { get; set; } = new();
    public TelemetryOptions Telemetry { get; set; } = new();
    public Dictionary<string, AgentDefinition> Agents { get; set; } = new();
    public Dictionary<string, WorkflowDefinition> Workflows { get; set; } = new();
    public SchedulingOptions Scheduling { get; set; } = new();
    public McpOptions Mcp { get; set; } = new();
    public WorkerOptions Worker { get; set; } = new();
    public DataverseOptions Dataverse { get; set; } = new();
}
