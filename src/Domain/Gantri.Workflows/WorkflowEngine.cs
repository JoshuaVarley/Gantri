using System.Diagnostics;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Workflows;
using Gantri.Telemetry;
using Gantri.Workflows.Steps;
using Microsoft.Extensions.Logging;

namespace Gantri.Workflows;

public sealed class WorkflowEngine : ILegacyWorkflowEngine
{
    private readonly IWorkflowDefinitionRegistry _registry;
    private readonly StepExecutor _stepExecutor;
    private readonly IHookPipeline _hookPipeline;
    private readonly WorkflowStateManager? _stateManager;
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(
        IWorkflowDefinitionRegistry registry,
        StepExecutor stepExecutor,
        IHookPipeline hookPipeline,
        ILogger<WorkflowEngine> logger,
        WorkflowStateManager? stateManager = null
    )
    {
        _registry = registry;
        _stepExecutor = stepExecutor;
        _hookPipeline = hookPipeline;
        _logger = logger;
        _stateManager = stateManager;
    }

    public async Task<WorkflowResult> ExecuteAsync(
        string workflowName,
        IReadOnlyDictionary<string, object?>? input = null,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = GantriActivitySources.Workflows.StartActivity(
            "gantri.workflows.execute"
        );
        activity?.SetTag(GantriSemanticConventions.WorkflowName, workflowName);

        var definition =
            _registry.TryGet(workflowName)
            ?? throw new InvalidOperationException(
                $"Workflow '{workflowName}' not found. Available: {string.Join(", ", _registry.Names)}"
            );

        var context = new WorkflowContext(workflowName, input);
        return await ExecuteFromStep(definition, context, 0, cancellationToken);
    }

    public async Task<WorkflowResult> ResumeAsync(
        string executionId,
        CancellationToken cancellationToken = default
    )
    {
        if (_stateManager is null)
            throw new InvalidOperationException(
                "Workflow state manager is not configured. Cannot resume."
            );

        var state = await _stateManager.LoadStateAsync(executionId, cancellationToken);
        if (state is null)
            throw new InvalidOperationException(
                $"No saved state found for execution '{executionId}'."
            );

        var definition =
            _registry.TryGet(state.WorkflowName)
            ?? throw new InvalidOperationException($"Workflow '{state.WorkflowName}' not found.");

        // Reconstruct context from saved state
        var context = new WorkflowContext(state.WorkflowName, state.Input);
        foreach (var (stepId, output) in state.StepOutputs)
        {
            context.SetStepOutput(stepId, output);
        }

        _logger.LogInformation(
            "Resuming workflow '{Workflow}' execution '{ExecutionId}' from step {StepIndex}",
            state.WorkflowName,
            executionId,
            state.CompletedStepIndex
        );

        return await ExecuteFromStep(
            definition,
            context,
            state.CompletedStepIndex,
            cancellationToken
        );
    }

    public IReadOnlyList<string> ListWorkflows() => _registry.Names;

    public async Task<IReadOnlyList<WorkflowRunInfo>> ListActiveRunsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (_stateManager is null)
            return [];

        var states = await _stateManager.ListActiveStatesAsync(cancellationToken);
        return states
            .Select(s =>
            {
                var totalSteps = _registry.TryGet(s.WorkflowName) is { } def ? def.Steps.Count : 0;
                return new WorkflowRunInfo
                {
                    ExecutionId = s.ExecutionId,
                    WorkflowName = s.WorkflowName,
                    Status = s.Status,
                    StartTime = s.StartTime,
                    CompletedSteps = s.CompletedStepIndex,
                    TotalSteps = totalSteps,
                };
            })
            .ToList();
    }

    public async Task<WorkflowRunStatus?> GetRunStatusAsync(
        string executionId,
        CancellationToken cancellationToken = default
    )
    {
        if (_stateManager is null)
            return null;

        var state = await _stateManager.LoadStateAsync(executionId, cancellationToken);
        if (state is null)
            return null;

        var totalSteps = _registry.TryGet(state.WorkflowName) is { } def ? def.Steps.Count : 0;
        return new WorkflowRunStatus
        {
            ExecutionId = state.ExecutionId,
            WorkflowName = state.WorkflowName,
            Status = state.Status,
            StartTime = state.StartTime,
            CompletedSteps = state.CompletedStepIndex,
            TotalSteps = totalSteps,
            CurrentStep = state.CurrentStep,
            Error = state.Error,
            StepOutputs = state.StepOutputs,
        };
    }

    private async Task<WorkflowResult> ExecuteFromStep(
        WorkflowDefinition definition,
        WorkflowContext context,
        int startIndex,
        CancellationToken cancellationToken
    )
    {
        var sw = Stopwatch.StartNew();
        GantriMeters.WorkflowsActive.Add(1);

        // Fire workflow-start hook
        var startEvent = new HookEvent(
            "workflow",
            context.WorkflowName,
            "workflow-start",
            HookTiming.Before
        );
        await _hookPipeline.ExecuteAsync(
            startEvent,
            _ => ValueTask.CompletedTask,
            cancellationToken: cancellationToken
        );

        _logger.LogInformation(
            "Starting workflow '{Workflow}' (execution: {ExecutionId})",
            context.WorkflowName,
            context.ExecutionId
        );

        try
        {
            for (var i = startIndex; i < definition.Steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = definition.Steps[i];

                // Checkpoint before each step
                if (_stateManager is not null)
                {
                    await _stateManager.SaveStateAsync(
                        new WorkflowState
                        {
                            ExecutionId = context.ExecutionId,
                            WorkflowName = context.WorkflowName,
                            Status = "running",
                            StartTime = DateTimeOffset.UtcNow - sw.Elapsed,
                            CompletedStepIndex = i,
                            Input = new Dictionary<string, object?>(
                                context.StepOutputs.Count > 0
                                    ? context.StepOutputs
                                    : new Dictionary<string, object?>()
                            ),
                            StepOutputs = new Dictionary<string, object?>(context.StepOutputs),
                            CurrentStep = step.Id,
                        },
                        cancellationToken
                    );
                }

                var result = await _stepExecutor.ExecuteStepAsync(step, context, cancellationToken);

                if (!result.Success)
                {
                    sw.Stop();
                    GantriMeters.WorkflowsActive.Add(-1);
                    _logger.LogWarning(
                        "Workflow '{Workflow}' failed at step '{Step}': {Error}",
                        context.WorkflowName,
                        step.Id,
                        result.Error
                    );

                    if (_stateManager is not null)
                    {
                        await _stateManager.SaveStateAsync(
                            new WorkflowState
                            {
                                ExecutionId = context.ExecutionId,
                                WorkflowName = context.WorkflowName,
                                Status = "failed",
                                CompletedStepIndex = i,
                                Error = result.Error,
                                StepOutputs = new Dictionary<string, object?>(context.StepOutputs),
                            },
                            cancellationToken
                        );
                    }

                    return WorkflowResult.Fail(
                        $"Step '{step.Id}' failed: {result.Error}",
                        sw.Elapsed,
                        context.ExecutionId
                    );
                }

                // Check if step is an approval gate (pause the workflow)
                if (result.Output is ApprovalPending)
                {
                    sw.Stop();
                    GantriMeters.WorkflowsActive.Add(-1);

                    if (_stateManager is not null)
                    {
                        await _stateManager.SaveStateAsync(
                            new WorkflowState
                            {
                                ExecutionId = context.ExecutionId,
                                WorkflowName = context.WorkflowName,
                                Status = "waiting_approval",
                                CompletedStepIndex = i + 1, // Next step to execute on resume
                                StepOutputs = new Dictionary<string, object?>(context.StepOutputs),
                                CurrentStep = step.Id,
                            },
                            cancellationToken
                        );
                    }

                    _logger.LogInformation(
                        "Workflow '{Workflow}' paused at approval gate '{Step}'",
                        context.WorkflowName,
                        step.Id
                    );
                    return WorkflowResult.Ok(
                        context.StepOutputs,
                        $"Waiting for approval at step '{step.Id}'",
                        sw.Elapsed,
                        context.ExecutionId
                    );
                }
            }

            sw.Stop();
            GantriMeters.WorkflowsActive.Add(-1);

            // The final output is the last step's output
            var lastStepId = definition.Steps.LastOrDefault()?.Id;
            var finalOutput = lastStepId is not null
                ? context.GetStepOutput(lastStepId)?.ToString()
                : null;

            // Clean up completed workflow state
            if (_stateManager is not null)
            {
                await _stateManager.RemoveStateAsync(context.ExecutionId, cancellationToken);
            }

            _logger.LogInformation(
                "Workflow '{Workflow}' completed in {Duration}ms",
                context.WorkflowName,
                sw.ElapsedMilliseconds
            );

            return WorkflowResult.Ok(
                context.StepOutputs,
                finalOutput,
                sw.Elapsed,
                context.ExecutionId
            );
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            GantriMeters.WorkflowsActive.Add(-1);
            return WorkflowResult.Fail("Workflow was cancelled.", sw.Elapsed, context.ExecutionId);
        }
    }
}
