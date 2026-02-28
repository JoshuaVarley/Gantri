using Gantri.Abstractions.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Gantri.Bridge;

/// <summary>
/// Implements <see cref="IAgentProvider"/> by delegating to <see cref="GantriAgentFactory"/>
/// and <see cref="IAgentDefinitionRegistry"/>. Gives AG-UI and A2A hosts direct access
/// to AF <see cref="AIAgent"/> instances with all Gantri concerns (plugins, hooks, security, resilience) wired in.
/// </summary>
public sealed class GantriAgentProvider : IAgentProvider
{
    private readonly GantriAgentFactory _agentFactory;
    private readonly IAgentDefinitionRegistry _registry;
    private readonly ILogger<GantriAgentProvider> _logger;

    public GantriAgentProvider(
        GantriAgentFactory agentFactory,
        IAgentDefinitionRegistry registry,
        ILogger<GantriAgentProvider> logger)
    {
        _agentFactory = agentFactory;
        _registry = registry;
        _logger = logger;
    }

    public IReadOnlyList<string> AgentNames => _registry.Names;

    public async Task<AIAgent> GetAgentAsync(string agentName, CancellationToken cancellationToken = default)
    {
        var definition = _registry.TryGet(agentName)
            ?? throw new InvalidOperationException(
                $"Agent '{agentName}' not found. Available agents: {string.Join(", ", _registry.Names)}");

        _logger.LogInformation("Creating AIAgent '{Agent}' via IAgentProvider", agentName);

        return await _agentFactory.CreateAgentAsync(definition, cancellationToken);
    }
}
