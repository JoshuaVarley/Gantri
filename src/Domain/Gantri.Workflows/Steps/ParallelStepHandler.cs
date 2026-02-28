using Gantri.Abstractions.Configuration;

namespace Gantri.Workflows.Steps;

/// <summary>
/// Executes child steps in parallel and waits for all to complete.
/// Uses Func&lt;StepExecutor&gt; to break the circular singleton dependency:
/// StepExecutor → IEnumerable&lt;IStepHandler&gt; → ParallelStepHandler → StepExecutor.
/// </summary>
public sealed class ParallelStepHandler : IStepHandler
{
    private readonly Func<StepExecutor> _stepExecutorFactory;

    public string StepType => "parallel";

    public ParallelStepHandler(Func<StepExecutor> stepExecutorFactory)
    {
        _stepExecutorFactory = stepExecutorFactory;
    }

    public async Task<StepResult> ExecuteAsync(WorkflowStepDefinition step, WorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (step.Steps.Count == 0)
            return StepResult.Fail($"Step '{step.Id}' is type 'parallel' but has no child steps.");

        var executor = _stepExecutorFactory();
        var tasks = step.Steps.Select(childStep =>
            executor.ExecuteStepAsync(childStep, context, cancellationToken));

        var results = await Task.WhenAll(tasks);

        var failures = results.Where(r => !r.Success).ToList();
        if (failures.Count > 0)
        {
            var errors = string.Join("; ", failures.Select(f => f.Error));
            return StepResult.Fail($"Parallel step '{step.Id}' had {failures.Count} failure(s): {errors}");
        }

        return StepResult.Ok($"{results.Length} parallel steps completed");
    }
}
