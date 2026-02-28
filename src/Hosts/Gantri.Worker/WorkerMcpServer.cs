using System.ComponentModel;
using System.Text.Json;
using Gantri.Abstractions.Plugins;
using Gantri.Abstractions.Scheduling;
using ModelContextProtocol.Server;

namespace Gantri.Worker;

/// <summary>
/// MCP server tools exposed by the Worker for remote management.
/// </summary>
[McpServerToolType]
public sealed class WorkerMcpServer
{
    private readonly IJobScheduler _scheduler;
    private readonly WorkerHealthService _healthService;
    private readonly IPluginRouter? _pluginRouter;

    public WorkerMcpServer(
        IJobScheduler scheduler,
        WorkerHealthService healthService,
        IPluginRouter? pluginRouter = null)
    {
        _scheduler = scheduler;
        _healthService = healthService;
        _pluginRouter = pluginRouter;
    }

    [McpServerTool(Name = "scheduler_list_jobs"), Description("List all scheduled jobs with their status")]
    public string SchedulerListJobs()
    {
        var jobs = _scheduler.ListJobs();
        return JsonSerializer.Serialize(jobs, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool(Name = "scheduler_trigger_job"), Description("Manually trigger a scheduled job by name")]
    public async Task<string> SchedulerTriggerJob(string jobName)
    {
        await _scheduler.TriggerAsync(jobName);
        return JsonSerializer.Serialize(new { status = "triggered", job = jobName });
    }

    [McpServerTool(Name = "scheduler_pause_job"), Description("Pause a scheduled job")]
    public async Task<string> SchedulerPauseJob(string jobName)
    {
        await _scheduler.PauseAsync(jobName);
        return JsonSerializer.Serialize(new { status = "paused", job = jobName });
    }

    [McpServerTool(Name = "scheduler_resume_job"), Description("Resume a paused scheduled job")]
    public async Task<string> SchedulerResumeJob(string jobName)
    {
        await _scheduler.ResumeAsync(jobName);
        return JsonSerializer.Serialize(new { status = "resumed", job = jobName });
    }

    [McpServerTool(Name = "scheduler_job_status"), Description("Get detailed status of a specific job")]
    public async Task<string> SchedulerJobStatus(string jobName)
    {
        var detail = await _scheduler.GetJobDetailAsync(jobName);
        if (detail is null)
            return JsonSerializer.Serialize(new { error = $"Job '{jobName}' not found" });
        return JsonSerializer.Serialize(detail, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool(Name = "worker_status"), Description("Get worker health status including uptime and memory")]
    public string WorkerStatus()
    {
        var status = _healthService.GetStatus();
        return JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool(Name = "worker_plugin_list"), Description("List all loaded plugins")]
    public async Task<string> WorkerPluginList()
    {
        if (_pluginRouter is null)
            return JsonSerializer.Serialize(new { plugins = Array.Empty<object>() });

        var plugins = await _pluginRouter.GetAllPluginsAsync();
        var result = plugins.Select(p => new
        {
            name = p.Name,
            version = p.Version,
            type = p.Type.ToString(),
            actions = p.ActionNames
        });
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
