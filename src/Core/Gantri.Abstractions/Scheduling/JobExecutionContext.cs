namespace Gantri.Abstractions.Scheduling;

public sealed class JobExecutionContext
{
    public string JobName { get; init; } = string.Empty;
    public string JobType { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset ScheduledTime { get; init; }
    public DateTimeOffset StartTime { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
    public CancellationToken CancellationToken { get; init; }
}
