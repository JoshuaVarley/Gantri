using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Workflows;
using Gantri.Telemetry;
using Microsoft.Extensions.Logging;

namespace Gantri.Workflows;

/// <summary>
/// Routes workflow steps to the appropriate handler based on step type.
/// </summary>
public sealed class StepExecutor
{
    private readonly Dictionary<string, IStepHandler> _handlers;
    private readonly IHookPipeline _hookPipeline;
    private readonly ILogger<StepExecutor> _logger;

    public StepExecutor(
        IEnumerable<IStepHandler> handlers,
        IHookPipeline hookPipeline,
        ILogger<StepExecutor> logger)
    {
        _handlers = handlers.ToDictionary(h => h.StepType, StringComparer.OrdinalIgnoreCase);
        _hookPipeline = hookPipeline;
        _logger = logger;
    }

    public async Task<StepResult> ExecuteStepAsync(WorkflowStepDefinition step, WorkflowContext context, CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.Workflows.StartActivity("gantri.workflows.step");
        activity?.SetTag(GantriSemanticConventions.WorkflowName, context.WorkflowName);
        activity?.SetTag(GantriSemanticConventions.WorkflowStepId, step.Id);
        activity?.SetTag(GantriSemanticConventions.WorkflowStepType, step.Type);

        if (!_handlers.TryGetValue(step.Type, out var handler))
            return StepResult.Fail($"Unknown step type '{step.Type}' for step '{step.Id}'.");

        // Fire step-start hook
        var hookEvent = new HookEvent("workflow", context.WorkflowName, "step-execute", HookTiming.Before);
        await _hookPipeline.ExecuteAsync(hookEvent, _ => ValueTask.CompletedTask, cancellationToken: cancellationToken);

        _logger.LogInformation("Executing step '{StepId}' (type: {StepType}) in workflow '{Workflow}'",
            step.Id, step.Type, context.WorkflowName);

        var result = await handler.ExecuteAsync(step, context, cancellationToken);

        if (result.Success)
        {
            context.SetStepOutput(step.Id, result.Output);
            GantriMeters.WorkflowStepsTotal.Add(1,
                new KeyValuePair<string, object?>(GantriSemanticConventions.WorkflowStepType, step.Type));
        }

        _logger.LogInformation("Step '{StepId}' completed (success: {Success})", step.Id, result.Success);

        return result;
    }
}
