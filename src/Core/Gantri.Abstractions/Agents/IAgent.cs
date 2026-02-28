namespace Gantri.Abstractions.Agents;

public interface IAgent
{
    string Name { get; }
    string Model { get; }
    string? Provider { get; }
    IReadOnlyList<string> Skills { get; }
    IReadOnlyList<string> AllowedActions { get; }
    IReadOnlyList<string> McpServers { get; }
}
