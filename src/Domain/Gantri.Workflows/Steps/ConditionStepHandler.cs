using Gantri.Abstractions.Configuration;

namespace Gantri.Workflows.Steps;

/// <summary>
/// Evaluates a simple condition expression. Returns the condition result as output.
/// Supports: "exists:steps.stepId" and "equals:steps.stepId.value:expected"
/// </summary>
public sealed class ConditionStepHandler : IStepHandler
{
    public string StepType => "condition";

    public Task<StepResult> ExecuteAsync(WorkflowStepDefinition step, WorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(step.Condition))
            return Task.FromResult(StepResult.Fail($"Step '{step.Id}' is type 'condition' but has no condition expression."));

        var resolved = context.ResolveTemplate(step.Condition);

        // Simple truthiness: non-empty and non-"false" is truthy
        var isTruthy = !string.IsNullOrWhiteSpace(resolved)
            && !resolved.Equals("false", StringComparison.OrdinalIgnoreCase)
            && !resolved.Equals("0", StringComparison.Ordinal);

        return Task.FromResult(StepResult.Ok(isTruthy));
    }
}
