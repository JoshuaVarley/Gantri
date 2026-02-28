namespace Gantri.Abstractions.Workflows;

public interface IWorkflowEngine
{
    Task<WorkflowResult> ExecuteAsync(
        string workflowName,
        IReadOnlyDictionary<string, object?>? input = null,
        CancellationToken cancellationToken = default
    );
    Task<WorkflowResult> ResumeAsync(
        string executionId,
        CancellationToken cancellationToken = default
    );
    IReadOnlyList<string> ListWorkflows();
    Task<IReadOnlyList<WorkflowRunInfo>> ListActiveRunsAsync(
        CancellationToken cancellationToken = default
    );
    Task<WorkflowRunStatus?> GetRunStatusAsync(
        string executionId,
        CancellationToken cancellationToken = default
    );
}

public interface ILegacyWorkflowEngine : IWorkflowEngine;

public sealed class WorkflowResult
{
    public bool Success { get; init; }
    public IReadOnlyDictionary<string, object?> StepOutputs { get; init; } =
        new Dictionary<string, object?>();
    public string? FinalOutput { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ExecutionId { get; init; }

    public static WorkflowResult Ok(
        IReadOnlyDictionary<string, object?> stepOutputs,
        string? finalOutput,
        TimeSpan duration,
        string? executionId = null
    ) =>
        new()
        {
            Success = true,
            StepOutputs = stepOutputs,
            FinalOutput = finalOutput,
            Duration = duration,
            ExecutionId = executionId,
        };

    public static WorkflowResult Fail(
        string error,
        TimeSpan duration,
        string? executionId = null
    ) =>
        new()
        {
            Success = false,
            Error = error,
            Duration = duration,
            ExecutionId = executionId,
        };
}

public sealed class WorkflowRunInfo
{
    public string ExecutionId { get; init; } = string.Empty;
    public string WorkflowName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset StartTime { get; init; }
    public int CompletedSteps { get; init; }
    public int TotalSteps { get; init; }
}

public sealed class WorkflowRunStatus
{
    public string ExecutionId { get; init; } = string.Empty;
    public string WorkflowName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset StartTime { get; init; }
    public int CompletedSteps { get; init; }
    public int TotalSteps { get; init; }
    public string? CurrentStep { get; init; }
    public string? Error { get; init; }
    public IReadOnlyDictionary<string, object?> StepOutputs { get; init; } =
        new Dictionary<string, object?>();
}
