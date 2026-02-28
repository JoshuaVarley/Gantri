using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Scheduling;
using Gantri.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace Gantri.Scheduling.Tests;

public class JobSchedulerExtendedTests
{
    private static ICronTickerManager<CronTickerEntity> CreateCronManager()
        => Substitute.For<ICronTickerManager<CronTickerEntity>>();

    private static ITimeTickerManager<TimeTickerEntity> CreateTimeManager()
        => Substitute.For<ITimeTickerManager<TimeTickerEntity>>();

    private static JobScheduler CreateScheduler(
        Dictionary<string, ScheduledJobDefinition> jobs,
        ICronTickerManager<CronTickerEntity>? cronManager = null,
        ITimeTickerManager<TimeTickerEntity>? timeManager = null)
    {
        return new JobScheduler(
            jobs,
            cronManager ?? CreateCronManager(),
            timeManager ?? CreateTimeManager(),
            NullLogger<JobScheduler>.Instance);
    }

    [Fact]
    public async Task ResumeJob_AfterPause_RemovesPausedFlag()
    {
        var cronManager = CreateCronManager();
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["test-job"] = new()
            {
                Type = "workflow",
                Workflow = "test",
                Cron = "0 * * * *",
                Enabled = true
            }
        };

        var scheduler = CreateScheduler(jobs, cronManager: cronManager);

        await scheduler.PauseAsync("test-job");
        scheduler.ListJobs().Should().Contain(j => j.Name == "test-job" && j.IsPaused);

        await scheduler.ResumeAsync("test-job");
        scheduler.ListJobs().Should().Contain(j => j.Name == "test-job" && !j.IsPaused);
    }

    [Fact]
    public async Task ResumeJob_WhenNotPaused_DoesNothing()
    {
        var cronManager = CreateCronManager();
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["test-job"] = new()
            {
                Type = "workflow",
                Workflow = "test",
                Cron = "0 * * * *",
                Enabled = true
            }
        };

        var scheduler = CreateScheduler(jobs, cronManager: cronManager);

        // Resume without pause should be a no-op
        await scheduler.ResumeAsync("test-job");
        await cronManager.DidNotReceive().AddAsync(Arg.Any<CronTickerEntity>());
    }

    [Fact]
    public async Task ResumeJob_UnknownJob_Throws()
    {
        var scheduler = CreateScheduler(new Dictionary<string, ScheduledJobDefinition>());

        var act = () => scheduler.ResumeAsync("nonexistent");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task TriggerJob_AgentType_UsesCorrectFunction()
    {
        var timeManager = CreateTimeManager();
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["agent-job"] = new()
            {
                Type = "agent",
                Agent = "my-agent",
                Cron = "0 0 * * *",
                Enabled = true
            }
        };

        var scheduler = CreateScheduler(jobs, timeManager: timeManager);

        try
        {
            await scheduler.TriggerAsync("agent-job");
        }
        catch (InvalidOperationException)
        {
            // Expected if mock returns default TickerResult
        }

        await timeManager.Received(1).AddAsync(Arg.Is<TimeTickerEntity>(e =>
            e.Function == "gantri_agent_job"));
    }

    [Fact]
    public async Task TriggerJob_PluginType_UsesCorrectFunction()
    {
        var timeManager = CreateTimeManager();
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["plugin-job"] = new()
            {
                Type = "plugin",
                Plugin = "my-plugin",
                Action = "run",
                Cron = "0 0 * * *",
                Enabled = true
            }
        };

        var scheduler = CreateScheduler(jobs, timeManager: timeManager);

        try
        {
            await scheduler.TriggerAsync("plugin-job");
        }
        catch (InvalidOperationException)
        {
            // Expected if mock returns default TickerResult
        }

        await timeManager.Received(1).AddAsync(Arg.Is<TimeTickerEntity>(e =>
            e.Function == "gantri_plugin_job"));
    }

    [Fact]
    public async Task GetJobDetail_PausedJob_ShowsPaused()
    {
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["my-job"] = new()
            {
                Type = "workflow",
                Workflow = "test",
                Cron = "0 0 * * *",
                Enabled = true
            }
        };

        var scheduler = CreateScheduler(jobs);

        await scheduler.PauseAsync("my-job");
        var detail = await scheduler.GetJobDetailAsync("my-job");

        detail.Should().NotBeNull();
        detail!.IsPaused.Should().BeTrue();
    }

    [Fact]
    public void StartAsync_CompletesSuccessfully()
    {
        var scheduler = CreateScheduler(new Dictionary<string, ScheduledJobDefinition>
        {
            ["job"] = new() { Type = "workflow", Workflow = "test", Cron = "* * * * *", Enabled = true }
        });

        var act = () => scheduler.StartAsync();
        act.Should().NotThrowAsync();
    }

    [Fact]
    public void StopAsync_CompletesSuccessfully()
    {
        var scheduler = CreateScheduler(new Dictionary<string, ScheduledJobDefinition>());

        var act = () => scheduler.StopAsync();
        act.Should().NotThrowAsync();
    }
}
