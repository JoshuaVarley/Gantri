using Gantri.Abstractions.Hooks;
using Microsoft.Extensions.DependencyInjection;

namespace Gantri.Hooks;

public static class HookServiceExtensions
{
    public static IServiceCollection AddGantriHooks(this IServiceCollection services)
    {
        services.AddSingleton<HookRegistry>();
        services.AddSingleton<HookExecutor>();
        services.AddSingleton<HookPipeline>();
        services.AddSingleton<IHookPipeline>(sp => sp.GetRequiredService<HookPipeline>());
        return services;
    }
}
