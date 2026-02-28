namespace Gantri.Abstractions.Scheduling;

public sealed record SchedulerEvent(
    string JobName,
    string JobType,
    string EventType,
    DateTimeOffset Timestamp,
    string? ExecutionId = null,
    TimeSpan? Duration = null,
    Exception? Error = null)
{
    public static SchedulerEvent JobStart(string jobName, string jobType, string executionId) =>
        new(jobName, jobType, "job-start", DateTimeOffset.UtcNow, executionId);

    public static SchedulerEvent JobComplete(string jobName, string jobType, string executionId, TimeSpan duration) =>
        new(jobName, jobType, "job-complete", DateTimeOffset.UtcNow, executionId, duration);

    public static SchedulerEvent JobError(string jobName, string jobType, string executionId, Exception error) =>
        new(jobName, jobType, "job-error", DateTimeOffset.UtcNow, executionId, Error: error);

    public static SchedulerEvent JobSkip(string jobName, string jobType) =>
        new(jobName, jobType, "job-skip", DateTimeOffset.UtcNow);
}
