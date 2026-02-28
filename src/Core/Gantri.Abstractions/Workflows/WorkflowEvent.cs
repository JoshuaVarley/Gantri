namespace Gantri.Abstractions.Workflows;

public sealed record WorkflowEvent(
    string WorkflowName,
    string EventType,
    DateTimeOffset Timestamp,
    string? StepId = null,
    string? StepType = null,
    IReadOnlyDictionary<string, object?>? Metadata = null)
{
    public static WorkflowEvent Start(string workflowName) =>
        new(workflowName, "start", DateTimeOffset.UtcNow);

    public static WorkflowEvent StepStart(string workflowName, string stepId, string stepType) =>
        new(workflowName, "step-start", DateTimeOffset.UtcNow, stepId, stepType);

    public static WorkflowEvent StepComplete(string workflowName, string stepId, string stepType) =>
        new(workflowName, "step-complete", DateTimeOffset.UtcNow, stepId, stepType);

    public static WorkflowEvent Complete(string workflowName) =>
        new(workflowName, "complete", DateTimeOffset.UtcNow);

    public static WorkflowEvent Error(string workflowName, string? stepId = null) =>
        new(workflowName, "error", DateTimeOffset.UtcNow, stepId);
}
