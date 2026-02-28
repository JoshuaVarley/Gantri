using System.Diagnostics;
using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Plugins;
using Gantri.Abstractions.Scheduling;
using Gantri.Abstractions.Workflows;
using Gantri.Telemetry;
using Microsoft.Extensions.Logging;

namespace Gantri.Scheduling;

/// <summary>
/// Executes a scheduled job by running its configured workflow, agent, or plugin.
/// Retained for direct invocation scenarios (e.g., CLI trigger) alongside TickerQ functions.
/// </summary>
public sealed class JobRunner
{
    private readonly IWorkflowEngine? _workflowEngine;
    private readonly IAgentOrchestrator? _agentOrchestrator;
    private readonly IPluginRouter? _pluginRouter;
    private readonly IHookPipeline _hookPipeline;
    private readonly ILogger<JobRunner> _logger;

    public JobRunner(
        IHookPipeline hookPipeline,
        ILogger<JobRunner> logger,
        IWorkflowEngine? workflowEngine = null,
        IAgentOrchestrator? agentOrchestrator = null,
        IPluginRouter? pluginRouter = null)
    {
        _hookPipeline = hookPipeline;
        _logger = logger;
        _workflowEngine = workflowEngine;
        _agentOrchestrator = agentOrchestrator;
        _pluginRouter = pluginRouter;
    }

    public async Task<JobExecutionResult> RunAsync(string jobName, ScheduledJobDefinition jobDef, CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.Scheduling.StartActivity("gantri.scheduling.run");
        activity?.SetTag(GantriSemanticConventions.SchedulerJob, jobName);
        activity?.SetTag(GantriSemanticConventions.SchedulerJobType, jobDef.Type);

        var executionId = Guid.NewGuid().ToString("N")[..12];
        var sw = Stopwatch.StartNew();

        // Fire job-start hook
        var hookEvent = new HookEvent("scheduler", jobName, "job-execute", HookTiming.Before);
        await _hookPipeline.ExecuteAsync(hookEvent, _ => ValueTask.CompletedTask, cancellationToken: cancellationToken);

        _logger.LogInformation("Running job '{Job}' (execution: {ExecutionId}, type: {Type})",
            jobName, executionId, jobDef.Type);

        GantriMeters.SchedulerJobsTotal.Add(1,
            new KeyValuePair<string, object?>(GantriSemanticConventions.SchedulerJobType, jobDef.Type));

        try
        {
            var result = jobDef.Type switch
            {
                "workflow" => await RunWorkflowJobAsync(jobDef, cancellationToken),
                "agent" => await RunAgentJobAsync(jobDef, cancellationToken),
                "plugin" => await RunPluginJobAsync(jobName, jobDef, cancellationToken),
                _ => JobExecutionResult.Fail($"Unknown job type '{jobDef.Type}'")
            };

            sw.Stop();
            GantriMeters.SchedulerJobsDuration.Record(sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation("Job '{Job}' completed in {Duration}ms (success: {Success})",
                jobName, sw.ElapsedMilliseconds, result.Success);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Job '{Job}' failed with exception", jobName);
            return JobExecutionResult.Fail(ex.Message);
        }
    }

    private async Task<JobExecutionResult> RunWorkflowJobAsync(ScheduledJobDefinition jobDef, CancellationToken cancellationToken)
    {
        if (_workflowEngine is null)
            return JobExecutionResult.Fail("Workflow engine not available.");

        if (string.IsNullOrWhiteSpace(jobDef.Workflow))
            return JobExecutionResult.Fail("Job type is 'workflow' but no workflow specified.");

        var result = await _workflowEngine.ExecuteAsync(jobDef.Workflow, jobDef.Parameters, cancellationToken);
        return result.Success
            ? JobExecutionResult.Ok(result.FinalOutput)
            : JobExecutionResult.Fail(result.Error ?? "Workflow failed");
    }

    private async Task<JobExecutionResult> RunAgentJobAsync(ScheduledJobDefinition jobDef, CancellationToken cancellationToken)
    {
        if (_agentOrchestrator is null)
            return JobExecutionResult.Fail("Agent orchestrator not available.");

        if (string.IsNullOrWhiteSpace(jobDef.Agent))
            return JobExecutionResult.Fail("Job type is 'agent' but no agent specified.");

        var input = jobDef.Input ?? "Execute scheduled task.";

        await using var session = await _agentOrchestrator.CreateSessionAsync(jobDef.Agent, cancellationToken);
        var response = await session.SendMessageAsync(input, cancellationToken);
        return JobExecutionResult.Ok(response);
    }

    private async Task<JobExecutionResult> RunPluginJobAsync(string jobName, ScheduledJobDefinition jobDef, CancellationToken cancellationToken)
    {
        if (_pluginRouter is null)
            return JobExecutionResult.Fail("Plugin router not available.");

        if (string.IsNullOrWhiteSpace(jobDef.Plugin))
            return JobExecutionResult.Fail("Job type is 'plugin' but no plugin specified.");

        if (string.IsNullOrWhiteSpace(jobDef.Action))
            return JobExecutionResult.Fail("Job type is 'plugin' but no action specified.");

        var plugin = await _pluginRouter.ResolveAsync(jobDef.Plugin, cancellationToken);
        var input = new PluginActionInput
        {
            ActionName = jobDef.Action,
            Parameters = jobDef.Parameters
        };
        var result = await plugin.ExecuteActionAsync(jobDef.Action, input, cancellationToken);
        return result.Success
            ? JobExecutionResult.Ok(result.Output?.ToString())
            : JobExecutionResult.Fail(result.Error ?? "Plugin action failed");
    }
}

public sealed class JobExecutionResult
{
    public bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }

    public static JobExecutionResult Ok(string? output = null) => new() { Success = true, Output = output };
    public static JobExecutionResult Fail(string error) => new() { Success = false, Error = error };
}
