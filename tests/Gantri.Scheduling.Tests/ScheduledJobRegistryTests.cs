using Gantri.Abstractions.Configuration;
using Gantri.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace Gantri.Scheduling.Tests;

public class ScheduledJobRegistryTests
{
    private static ICronTickerManager<CronTickerEntity> CreateCronManager()
    {
        // TickerResult<T> cannot be constructed or substituted (sealed, no parameterless ctor).
        // The default mock returns null for AddAsync, which SeedJobsAsync handles gracefully.
        return Substitute.For<ICronTickerManager<CronTickerEntity>>();
    }

    [Fact]
    public async Task SeedJobsAsync_WorkflowJob_CreatesCronTicker()
    {
        var cronManager = CreateCronManager();
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["nightly-review"] = new()
            {
                Type = "workflow",
                Workflow = "review-and-triage",
                Cron = "0 2 * * *",
                Enabled = true
            }
        };

        await ScheduledJobRegistry.SeedJobsAsync(cronManager, jobs, NullLogger.Instance);

        await cronManager.Received(1).AddAsync(Arg.Is<CronTickerEntity>(e =>
            e.Function == "gantri_workflow_job" &&
            e.Expression == "0 2 * * *" &&
            e.Retries == 3));
    }

    [Fact]
    public async Task SeedJobsAsync_AgentJob_UsesCorrectFunction()
    {
        var cronManager = CreateCronManager();
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["hourly-triage"] = new()
            {
                Type = "agent",
                Agent = "ticket-triager",
                Cron = "0 * * * *",
                Enabled = true
            }
        };

        await ScheduledJobRegistry.SeedJobsAsync(cronManager, jobs, NullLogger.Instance);

        await cronManager.Received(1).AddAsync(Arg.Is<CronTickerEntity>(e =>
            e.Function == "gantri_agent_job"));
    }

    [Fact]
    public async Task SeedJobsAsync_PluginJob_UsesCorrectFunction()
    {
        var cronManager = CreateCronManager();
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["daily-cleanup"] = new()
            {
                Type = "plugin",
                Plugin = "cleanup-plugin",
                Action = "run",
                Cron = "0 3 * * *",
                Enabled = true
            }
        };

        await ScheduledJobRegistry.SeedJobsAsync(cronManager, jobs, NullLogger.Instance);

        await cronManager.Received(1).AddAsync(Arg.Is<CronTickerEntity>(e =>
            e.Function == "gantri_plugin_job"));
    }

    [Fact]
    public async Task SeedJobsAsync_DisabledJob_SkipsSeeding()
    {
        var cronManager = CreateCronManager();
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["disabled-job"] = new()
            {
                Type = "workflow",
                Workflow = "test",
                Cron = "0 0 * * *",
                Enabled = false
            }
        };

        await ScheduledJobRegistry.SeedJobsAsync(cronManager, jobs, NullLogger.Instance);

        await cronManager.DidNotReceive().AddAsync(Arg.Any<CronTickerEntity>());
    }

    [Fact]
    public async Task SeedJobsAsync_EmptyCron_SkipsSeeding()
    {
        var cronManager = CreateCronManager();
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["no-cron"] = new()
            {
                Type = "workflow",
                Workflow = "test",
                Cron = "",
                Enabled = true
            }
        };

        await ScheduledJobRegistry.SeedJobsAsync(cronManager, jobs, NullLogger.Instance);

        await cronManager.DidNotReceive().AddAsync(Arg.Any<CronTickerEntity>());
    }

    [Fact]
    public async Task SeedJobsAsync_UnknownType_SkipsSeeding()
    {
        var cronManager = CreateCronManager();
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["bad-type"] = new()
            {
                Type = "unknown",
                Cron = "0 0 * * *",
                Enabled = true
            }
        };

        await ScheduledJobRegistry.SeedJobsAsync(cronManager, jobs, NullLogger.Instance);

        await cronManager.DidNotReceive().AddAsync(Arg.Any<CronTickerEntity>());
    }

    [Fact]
    public async Task SeedJobsAsync_MultipleJobs_SeedsAllEnabled()
    {
        var cronManager = CreateCronManager();
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["job-a"] = new() { Type = "workflow", Workflow = "a", Cron = "0 1 * * *", Enabled = true },
            ["job-b"] = new() { Type = "agent", Agent = "b", Cron = "0 2 * * *", Enabled = true },
            ["job-c"] = new() { Type = "workflow", Workflow = "c", Cron = "0 3 * * *", Enabled = false }
        };

        await ScheduledJobRegistry.SeedJobsAsync(cronManager, jobs, NullLogger.Instance);

        await cronManager.Received(2).AddAsync(Arg.Any<CronTickerEntity>());
    }
}
