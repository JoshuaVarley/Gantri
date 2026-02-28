using Gantri.Abstractions.Configuration;

namespace Gantri.Configuration;

public sealed class WorkflowDefinitionRegistry : IWorkflowDefinitionRegistry
{
    private readonly Dictionary<string, WorkflowDefinition> _definitions;

    public WorkflowDefinitionRegistry(Dictionary<string, WorkflowDefinition>? definitions = null)
    {
        _definitions = definitions ?? new();
    }

    public WorkflowDefinition? TryGet(string name) =>
        _definitions.TryGetValue(name, out var def) ? def : null;

    public IReadOnlyDictionary<string, WorkflowDefinition> GetAll() => _definitions;

    public bool Contains(string name) => _definitions.ContainsKey(name);

    public IReadOnlyList<string> Names => _definitions.Keys.ToList();
}
