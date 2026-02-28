using Gantri.Abstractions.Plugins;
using Gantri.Plugins.Native;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // Plugin services — DI-backed service provider for plugins
        services.AddSingleton<Gantri.Abstractions.Plugins.IPluginServices>(sp =>
            new DefaultPluginServices(sp, sp.GetRequiredService<ILoggerFactory>()));

        return services;
    }
}
