namespace Gantri.Abstractions.Configuration;

public interface IAgentDefinitionRegistry
{
    AgentDefinition? TryGet(string name);
    IReadOnlyDictionary<string, AgentDefinition> GetAll();
    bool Contains(string name);
    IReadOnlyList<string> Names { get; }
}
