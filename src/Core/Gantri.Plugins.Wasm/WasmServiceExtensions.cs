using Gantri.Abstractions.Mcp;
using Gantri.Abstractions.Plugins;
using Gantri.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Gantri.Plugins.Wasm;

public static class WasmServiceExtensions
{
    public static IServiceCollection AddGantriWasmPlugins(this IServiceCollection services)
    {
        services.AddSingleton<WasmPluginHost>();
        services.AddSingleton<PluginCapabilityManager>();
        services.AddSingleton<HostFunctionRegistry>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
            var logger = new Microsoft.Extensions.Logging.Logger<HostFunctionRegistry>(loggerFactory);

            // Resolve host services: prefer explicitly registered implementations,
            // otherwise build from underlying dependencies when available.
            var aiService = sp.GetService<IHostAiService>()
                ?? (sp.GetService<IChatClient>() is { } chatClient ? new HostAiService(chatClient) : null);
            var configService = sp.GetService<IHostConfigService>()
                ?? (sp.GetService<GantriConfigRoot>() is { } config ? new HostConfigService(config) : null);
            var mcpService = sp.GetService<IHostMcpService>()
                ?? (sp.GetService<IMcpToolProvider>() is { } mcpProvider ? new HostMcpService(mcpProvider) : null);

            var httpClientFactory = sp.GetService<IHttpClientFactory>();
            return new HostFunctionRegistry(logger, aiService, configService, mcpService, httpClientFactory);
        });
        services.AddSingleton<WasmPluginLoader>();
        services.AddSingleton<IPluginLoader>(sp => sp.GetRequiredService<WasmPluginLoader>());
        return services;
    }
}
