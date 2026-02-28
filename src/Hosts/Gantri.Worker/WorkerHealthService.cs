using System.Diagnostics;

namespace Gantri.Worker;

/// <summary>
/// Tracks worker health metrics: uptime, active jobs, memory usage.
/// </summary>
public sealed class WorkerHealthService
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private int _activeJobs;

    public TimeSpan Uptime => DateTimeOffset.UtcNow - _startTime;
    public int ActiveJobs => _activeJobs;
    public long MemoryUsageBytes => Process.GetCurrentProcess().WorkingSet64;
    public string NodeName => Environment.MachineName;

    public void IncrementActiveJobs() => Interlocked.Increment(ref _activeJobs);
    public void DecrementActiveJobs() => Interlocked.Decrement(ref _activeJobs);

    public WorkerStatusInfo GetStatus()
    {
        return new WorkerStatusInfo
        {
            Node = NodeName,
            UptimeSeconds = (long)Uptime.TotalSeconds,
            ActiveJobs = ActiveJobs,
            MemoryUsageMb = MemoryUsageBytes / (1024.0 * 1024.0)
        };
    }
}

public sealed class WorkerStatusInfo
{
    public string Node { get; init; } = string.Empty;
    public long UptimeSeconds { get; init; }
    public int ActiveJobs { get; init; }
    public double MemoryUsageMb { get; init; }
}
