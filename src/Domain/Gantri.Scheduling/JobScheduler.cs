using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace Gantri.Scheduling;

/// <summary>
/// Manages scheduled jobs via TickerQ. Delegates persistence, retries, and distributed
/// locking to the TickerQ infrastructure.
/// </summary>
public sealed class JobScheduler : IJobScheduler
{
    private readonly Dictionary<string, ScheduledJobDefinition> _jobs;
    private readonly ICronTickerManager<CronTickerEntity> _cronManager;
    private readonly ITimeTickerManager<TimeTickerEntity> _timeManager;
    private readonly ILogger<JobScheduler> _logger;
    private readonly Dictionary<string, Guid> _cronTickerIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pausedJobs = new(StringComparer.OrdinalIgnoreCase);

    public JobScheduler(
        Dictionary<string, ScheduledJobDefinition> jobs,
        ICronTickerManager<CronTickerEntity> cronManager,
        ITimeTickerManager<TimeTickerEntity> timeManager,
        ILogger<JobScheduler> logger)
    {
        _jobs = jobs;
        _cronManager = cronManager;
        _timeManager = timeManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Job scheduler started with {Count} jobs (TickerQ-backed)", _jobs.Count);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Job scheduler stopped");
        return Task.CompletedTask;
    }

    public IReadOnlyList<ScheduledJobInfo> ListJobs()
    {
        return _jobs.Select(kvp => new ScheduledJobInfo
        {
            Name = kvp.Key,
            Type = kvp.Value.Type,
            CronExpression = kvp.Value.Cron,
            IsEnabled = kvp.Value.Enabled,
            IsPaused = _pausedJobs.Contains(kvp.Key)
        }).ToList();
    }

    public async Task TriggerAsync(string jobName, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobName, out var jobDef))
            throw new InvalidOperationException($"Job '{jobName}' not found.");

        var functionName = GetFunctionName(jobDef.Type);
        var request = CreatePayload(jobName, jobDef);

        _logger.LogInformation("Manually triggering job '{Job}' via TickerQ one-shot", jobName);

        var entity = new TimeTickerEntity
        {
            Function = functionName,
            ExecutionTime = DateTime.UtcNow,
            Request = request is not null ? TickerHelper.CreateTickerRequest(request) : null,
            Retries = 3,
            RetryIntervals = [60, 300, 900]
        };

        var result = await _timeManager.AddAsync(entity);
        if (result is null || !result.IsSucceeded)
        {
            throw new InvalidOperationException(
                $"Failed to trigger job '{jobName}': {result?.Exception?.Message ?? "Unknown error"}");
        }
    }

    public async Task PauseAsync(string jobName, CancellationToken cancellationToken = default)
    {
        if (!_jobs.ContainsKey(jobName))
            throw new InvalidOperationException($"Job '{jobName}' not found.");

        if (_cronTickerIds.TryGetValue(jobName, out var tickerId))
        {
            await _cronManager.DeleteAsync(tickerId);
            _cronTickerIds.Remove(jobName);
        }

        _pausedJobs.Add(jobName);
        _logger.LogInformation("Paused job '{Job}'", jobName);
    }

    public async Task ResumeAsync(string jobName, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobName, out var jobDef))
            throw new InvalidOperationException($"Job '{jobName}' not found.");

        if (!_pausedJobs.Remove(jobName))
        {
            _logger.LogDebug("Job '{Job}' was not paused", jobName);
            return;
        }

        var functionName = GetFunctionName(jobDef.Type);
        var request = CreatePayload(jobName, jobDef);

        var entity = new CronTickerEntity
        {
            Function = functionName,
            Expression = jobDef.Cron,
            Request = request is not null ? TickerHelper.CreateTickerRequest(request) : null,
            Retries = 3,
            RetryIntervals = [60, 300, 900]
        };

        var result = await _cronManager.AddAsync(entity);
        if (result is not null && result.IsSucceeded)
        {
            _cronTickerIds[jobName] = result.Result.Id;
            _logger.LogInformation("Resumed job '{Job}'", jobName);
        }
    }

    public Task<ScheduledJobDetail?> GetJobDetailAsync(string jobName, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobName, out var jobDef))
            return Task.FromResult<ScheduledJobDetail?>(null);

        var detail = new ScheduledJobDetail
        {
            Name = jobName,
            Type = jobDef.Type,
            CronExpression = jobDef.Cron,
            IsEnabled = jobDef.Enabled,
            IsPaused = _pausedJobs.Contains(jobName)
        };

        return Task.FromResult<ScheduledJobDetail?>(detail);
    }

    internal void TrackCronTickerId(string jobName, Guid tickerId)
    {
        _cronTickerIds[jobName] = tickerId;
    }

    private static string GetFunctionName(string jobType)
    {
        return jobType switch
        {
            "workflow" => "gantri_workflow_job",
            "agent" => "gantri_agent_job",
            "plugin" => "gantri_plugin_job",
            _ => throw new InvalidOperationException($"Unknown job type '{jobType}'")
        };
    }

    private static object? CreatePayload(string jobName, ScheduledJobDefinition def)
    {
        return def.Type switch
        {
            "workflow" => new WorkflowJobPayload
            {
                WorkflowName = def.Workflow ?? jobName,
                Parameters = def.Parameters
            },
            "agent" => new AgentJobPayload
            {
                AgentName = def.Agent ?? jobName,
                Input = def.Input
            },
            "plugin" => new PluginJobPayload
            {
                PluginName = def.Plugin ?? string.Empty,
                ActionName = def.Action ?? string.Empty,
                Parameters = def.Parameters
            },
            _ => null
        };
    }
}
