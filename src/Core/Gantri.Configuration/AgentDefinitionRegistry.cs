using Gantri.Abstractions.Configuration;

namespace Gantri.Configuration;

public sealed class AgentDefinitionRegistry : IAgentDefinitionRegistry
{
    private readonly Dictionary<string, AgentDefinition> _definitions;

    public AgentDefinitionRegistry(Dictionary<string, AgentDefinition>? definitions = null)
    {
        _definitions = definitions ?? new();
    }

    public AgentDefinition? TryGet(string name) =>
        _definitions.TryGetValue(name, out var def) ? def : null;

    public IReadOnlyDictionary<string, AgentDefinition> GetAll() => _definitions;

    public bool Contains(string name) => _definitions.ContainsKey(name);

    public IReadOnlyList<string> Names => _definitions.Keys.ToList();
}
