using System.Diagnostics;
using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Hooks;
using Gantri.Abstractions.Plugins;
using Gantri.Abstractions.Workflows;
using Gantri.Telemetry;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Base;

namespace Gantri.Scheduling;

public sealed class WorkflowJobPayload
{
    public string WorkflowName { get; set; } = string.Empty;
    public Dictionary<string, object?> Parameters { get; set; } = new();
}

public sealed class AgentJobPayload
{
    public string AgentName { get; set; } = string.Empty;
    public string? Input { get; set; }
}

public sealed class PluginJobPayload
{
    public string PluginName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public Dictionary<string, object?> Parameters { get; set; } = new();
}

public sealed class TickerJobFunctions
{
    private readonly IWorkflowEngine? _workflowEngine;
    private readonly IAgentOrchestrator? _agentOrchestrator;
    private readonly IPluginRouter? _pluginRouter;
    private readonly IHookPipeline _hookPipeline;
    private readonly ILogger<TickerJobFunctions> _logger;

    public TickerJobFunctions(
        IHookPipeline hookPipeline,
        ILogger<TickerJobFunctions> logger,
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

    [TickerFunction("gantri_workflow_job")]
    public async Task ExecuteWorkflowJobAsync(
        TickerFunctionContext<WorkflowJobPayload> context,
        CancellationToken cancellationToken)
    {
        var payload = context.Request;
        using var activity = GantriActivitySources.Scheduling.StartActivity("gantri.scheduling.workflow");
        activity?.SetTag(GantriSemanticConventions.SchedulerJob, payload.WorkflowName);
        activity?.SetTag(GantriSemanticConventions.SchedulerJobType, "workflow");

        var sw = Stopwatch.StartNew();
        var hookEvent = new HookEvent("scheduler", payload.WorkflowName, "job-execute", HookTiming.Before);
        await _hookPipeline.ExecuteAsync(hookEvent, _ => ValueTask.CompletedTask, cancellationToken: cancellationToken);

        _logger.LogInformation("Executing workflow job '{Workflow}' (ticker: {Id})", payload.WorkflowName, context.Id);
        GantriMeters.SchedulerJobsTotal.Add(1,
            new KeyValuePair<string, object?>(GantriSemanticConventions.SchedulerJobType, "workflow"));

        if (_workflowEngine is null)
            throw new InvalidOperationException("Workflow engine not available.");

        var result = await _workflowEngine.ExecuteAsync(payload.WorkflowName, payload.Parameters, cancellationToken);
        sw.Stop();
        GantriMeters.SchedulerJobsDuration.Record(sw.Elapsed.TotalMilliseconds);

        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "Workflow failed");

        _logger.LogInformation("Workflow job '{Workflow}' completed in {Duration}ms", payload.WorkflowName, sw.ElapsedMilliseconds);
    }

    [TickerFunction("gantri_agent_job")]
    public async Task ExecuteAgentJobAsync(
        TickerFunctionContext<AgentJobPayload> context,
        CancellationToken cancellationToken)
    {
        var payload = context.Request;
        using var activity = GantriActivitySources.Scheduling.StartActivity("gantri.scheduling.agent");
        activity?.SetTag(GantriSemanticConventions.SchedulerJob, payload.AgentName);
        activity?.SetTag(GantriSemanticConventions.SchedulerJobType, "agent");

        var sw = Stopwatch.StartNew();
        var hookEvent = new HookEvent("scheduler", payload.AgentName, "job-execute", HookTiming.Before);
        await _hookPipeline.ExecuteAsync(hookEvent, _ => ValueTask.CompletedTask, cancellationToken: cancellationToken);

        _logger.LogInformation("Executing agent job '{Agent}' (ticker: {Id})", payload.AgentName, context.Id);
        GantriMeters.SchedulerJobsTotal.Add(1,
            new KeyValuePair<string, object?>(GantriSemanticConventions.SchedulerJobType, "agent"));

        if (_agentOrchestrator is null)
            throw new InvalidOperationException("Agent orchestrator not available.");

        var input = payload.Input ?? "Execute scheduled task.";
        await using var session = await _agentOrchestrator.CreateSessionAsync(payload.AgentName, cancellationToken);
        await session.SendMessageAsync(input, cancellationToken);

        sw.Stop();
        GantriMeters.SchedulerJobsDuration.Record(sw.Elapsed.TotalMilliseconds);
        _logger.LogInformation("Agent job '{Agent}' completed in {Duration}ms", payload.AgentName, sw.ElapsedMilliseconds);
    }

    [TickerFunction("gantri_plugin_job")]
    public async Task ExecutePluginJobAsync(
        TickerFunctionContext<PluginJobPayload> context,
        CancellationToken cancellationToken)
    {
        var payload = context.Request;
        using var activity = GantriActivitySources.Scheduling.StartActivity("gantri.scheduling.plugin");
        activity?.SetTag(GantriSemanticConventions.SchedulerJob, payload.PluginName);
        activity?.SetTag(GantriSemanticConventions.SchedulerJobType, "plugin");

        var sw = Stopwatch.StartNew();
        var hookEvent = new HookEvent("scheduler", payload.PluginName, "job-execute", HookTiming.Before);
        await _hookPipeline.ExecuteAsync(hookEvent, _ => ValueTask.CompletedTask, cancellationToken: cancellationToken);

        _logger.LogInformation("Executing plugin job '{Plugin}.{Action}' (ticker: {Id})",
            payload.PluginName, payload.ActionName, context.Id);
        GantriMeters.SchedulerJobsTotal.Add(1,
            new KeyValuePair<string, object?>(GantriSemanticConventions.SchedulerJobType, "plugin"));

        if (_pluginRouter is null)
            throw new InvalidOperationException("Plugin router not available.");

        var plugin = await _pluginRouter.ResolveAsync(payload.PluginName, cancellationToken);
        var actionInput = new PluginActionInput
        {
            ActionName = payload.ActionName,
            Parameters = payload.Parameters
        };
        var result = await plugin.ExecuteActionAsync(payload.ActionName, actionInput, cancellationToken);

        sw.Stop();
        GantriMeters.SchedulerJobsDuration.Record(sw.Elapsed.TotalMilliseconds);

        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "Plugin action failed");

        _logger.LogInformation("Plugin job '{Plugin}.{Action}' completed in {Duration}ms",
            payload.PluginName, payload.ActionName, sw.ElapsedMilliseconds);
    }
}
