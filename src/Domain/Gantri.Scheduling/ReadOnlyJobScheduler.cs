using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Scheduling;

namespace Gantri.Scheduling;

/// <summary>
/// Lightweight read-only job scheduler that reads job definitions from config
/// without requiring TickerQ infrastructure. Suitable for CLI commands that
/// only need to list/inspect jobs.
/// </summary>
public sealed class ReadOnlyJobScheduler : IJobScheduler
{
    private readonly Dictionary<string, ScheduledJobDefinition> _jobs;

    public ReadOnlyJobScheduler(Dictionary<string, ScheduledJobDefinition> jobs)
    {
        _jobs = jobs;
    }

    public IReadOnlyList<ScheduledJobInfo> ListJobs()
    {
        return _jobs.Select(kvp => new ScheduledJobInfo
        {
            Name = kvp.Key,
            Type = kvp.Value.Type,
            CronExpression = kvp.Value.Cron,
            IsEnabled = kvp.Value.Enabled
        }).ToList();
    }

    public Task<ScheduledJobDetail?> GetJobDetailAsync(string jobName, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobName, out var jobDef))
            return Task.FromResult<ScheduledJobDetail?>(null);

        return Task.FromResult<ScheduledJobDetail?>(new ScheduledJobDetail
        {
            Name = jobName,
            Type = jobDef.Type,
            CronExpression = jobDef.Cron,
            IsEnabled = jobDef.Enabled
        });
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task TriggerAsync(string jobName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Read-only scheduler cannot trigger jobs. Use the worker CLI commands instead.");
    public Task PauseAsync(string jobName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Read-only scheduler cannot pause jobs. Use the worker CLI commands instead.");
    public Task ResumeAsync(string jobName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Read-only scheduler cannot resume jobs. Use the worker CLI commands instead.");
}
