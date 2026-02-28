using Microsoft.Extensions.DependencyInjection;

namespace Gantri.Agents;

public static class AgentServiceExtensions
{
    public static IServiceCollection AddGantriAgents(this IServiceCollection services)
    {
        // Domain-only agent registrations (currently none — agent types live in Abstractions).
        // Bridge layer registers GantriAgentFactory and orchestrators.
        return services;
    }
}
