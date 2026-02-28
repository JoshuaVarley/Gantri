namespace Gantri.Abstractions.Configuration;

public interface IWorkflowDefinitionRegistry
{
    WorkflowDefinition? TryGet(string name);
    IReadOnlyDictionary<string, WorkflowDefinition> GetAll();
    bool Contains(string name);
    IReadOnlyList<string> Names { get; }
}
