using Microsoft.Extensions.DependencyInjection;

namespace Gantri.AI;

public static class AIServiceExtensions
{
    public static IServiceCollection AddGantriAI(this IServiceCollection services)
    {
        services.AddSingleton<ModelProviderRegistry>();
        return services;
    }
}
