using Gantri.Abstractions.Scheduling;

namespace Gantri.Worker;

/// <summary>
/// Hosted service that starts and stops the job scheduler with the application lifecycle.
/// </summary>
public sealed class SchedulerHostedService : BackgroundService
{
    private readonly IJobScheduler _scheduler;
    private readonly ILogger<SchedulerHostedService> _logger;

    public SchedulerHostedService(IJobScheduler scheduler, ILogger<SchedulerHostedService> logger)
    {
        _scheduler = scheduler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduler hosted service starting");

        await _scheduler.StartAsync(stoppingToken);

        // Keep alive until shutdown
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        await _scheduler.StopAsync(CancellationToken.None);
        _logger.LogInformation("Scheduler hosted service stopped");
    }
}
