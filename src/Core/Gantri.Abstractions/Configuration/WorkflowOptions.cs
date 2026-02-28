namespace Gantri.Abstractions.Configuration;

public sealed class WorkflowDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Trigger { get; set; } = "manual";
    public List<WorkflowStepDefinition> Steps { get; set; } = [];
}

public sealed class WorkflowStepDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // agent, plugin, condition, parallel
    public string? Agent { get; set; }
    public string? Plugin { get; set; }
    public string? Action { get; set; }
    public string? Input { get; set; }
    public string? Condition { get; set; }
    public List<WorkflowStepDefinition> Steps { get; set; } = []; // For parallel steps
    public Dictionary<string, object?> Parameters { get; set; } = new();
}
