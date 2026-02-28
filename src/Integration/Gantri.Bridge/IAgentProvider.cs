using Microsoft.Agents.AI;

namespace Gantri.Bridge;

/// <summary>
/// Exposes raw <see cref="AIAgent"/> creation for hosts that need direct access to AF agents
/// (e.g., AG-UI and A2A endpoints). Unlike <see cref="Gantri.Abstractions.Agents.IAgentSession"/>
/// which flattens responses to strings, this interface preserves AF's full capabilities.
/// </summary>
public interface IAgentProvider
{
    /// <summary>
    /// Creates an AF <see cref="AIAgent"/> for the named agent definition.
    /// The returned agent can be used with MapAGUI, MapA2A, or any AF hosting mechanism.
    /// </summary>
    Task<AIAgent> GetAgentAsync(string agentName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all available agent names from the definition registry.
    /// </summary>
    IReadOnlyList<string> AgentNames { get; }
}
