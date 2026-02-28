namespace Gantri.Abstractions.Scheduling;

public interface IJobScheduler
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<ScheduledJobInfo> ListJobs();
    Task TriggerAsync(string jobName, CancellationToken cancellationToken = default);
    Task PauseAsync(string jobName, CancellationToken cancellationToken = default);
    Task ResumeAsync(string jobName, CancellationToken cancellationToken = default);
    Task<ScheduledJobDetail?> GetJobDetailAsync(string jobName, CancellationToken cancellationToken = default);
}

public sealed class ScheduledJobInfo
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string CronExpression { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public bool IsPaused { get; init; }
    public DateTimeOffset? LastRun { get; init; }
    public DateTimeOffset? NextRun { get; init; }
}

public sealed class ScheduledJobDetail
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string CronExpression { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public bool IsPaused { get; init; }
    public DateTimeOffset? LastRun { get; init; }
    public DateTimeOffset? NextRun { get; init; }
    public int TotalExecutions { get; init; }
    public int FailedExecutions { get; init; }
    public string? LastError { get; init; }
}
