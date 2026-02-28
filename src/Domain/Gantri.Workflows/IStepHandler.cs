using Gantri.Abstractions.Configuration;

namespace Gantri.Workflows;

/// <summary>
/// Handles execution of a specific workflow step type (agent, plugin, condition, parallel).
/// </summary>
public interface IStepHandler
{
    string StepType { get; }
    Task<StepResult> ExecuteAsync(WorkflowStepDefinition step, WorkflowContext context, CancellationToken cancellationToken = default);
}

public sealed class StepResult
{
    public bool Success { get; init; }
    public object? Output { get; init; }
    public string? Error { get; init; }

    public static StepResult Ok(object? output = null) => new() { Success = true, Output = output };
    public static StepResult Fail(string error) => new() { Success = false, Error = error };
}
