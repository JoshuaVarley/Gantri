using Gantri.Abstractions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gantri.Workflows.Steps;

/// <summary>
/// Handles "approval" step type. Checkpoints the workflow and waits for external signal.
/// When the step is reached, the workflow state is saved with status "waiting_approval".
/// Resumption requires calling WorkflowEngine.ResumeAsync with the execution ID.
/// </summary>
public sealed class ApprovalStepHandler : IStepHandler
{
    private readonly ILogger<ApprovalStepHandler> _logger;

    public string StepType => "approval";

    public ApprovalStepHandler(ILogger<ApprovalStepHandler> logger)
    {
        _logger = logger;
    }

    public Task<StepResult> ExecuteAsync(WorkflowStepDefinition step, WorkflowContext context, CancellationToken cancellationToken = default)
    {
        var message = step.Input ?? $"Approval required for step '{step.Id}'";
        var resolvedMessage = context.ResolveTemplate(message);

        _logger.LogInformation("Workflow '{Workflow}' paused at approval step '{Step}': {Message}",
            context.WorkflowName, step.Id, resolvedMessage);

        // Return a special result that signals the workflow engine to pause
        return Task.FromResult(StepResult.Ok(new ApprovalPending
        {
            StepId = step.Id,
            Message = resolvedMessage,
            ExecutionId = context.ExecutionId
        }));
    }
}

public sealed class ApprovalPending
{
    public string StepId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
}
