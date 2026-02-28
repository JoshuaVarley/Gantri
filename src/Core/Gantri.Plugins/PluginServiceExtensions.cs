using Gantri.Abstractions.Plugins;
using Gantri.Plugins.Native;
using Microsoft.Extensions.DependencyInjection;

namespace Gantri.Plugins;

public static class PluginServiceExtensions
{
    public static IServiceCollection AddGantriPlugins(this IServiceCollection services)
    {
        // Native plugin infrastructure
        services.AddSingleton<NativePluginValidator>();
        services.AddSingleton<NativePluginLoader>();
        services.AddSingleton<IPluginLoader>(sp => sp.GetRequiredService<NativePluginLoader>());

        // Plugin router and management
        services.AddSingleton<PluginDiscovery>();
        services.AddSingleton<PluginManager>();
        services.AddSingleton<PluginRouter>();
        services.AddSingleton<IPluginRouter>(sp => sp.GetRequiredService<PluginRouter>());

        return services;
    }
}
