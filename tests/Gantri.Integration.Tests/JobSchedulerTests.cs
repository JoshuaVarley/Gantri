using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Scheduling;
using Gantri.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace Gantri.Integration.Tests;

public class JobSchedulerTests
{
    private static ICronTickerManager<CronTickerEntity> CreateCronManager()
    {
        return Substitute.For<ICronTickerManager<CronTickerEntity>>();
    }

    private static ITimeTickerManager<TimeTickerEntity> CreateTimeManager()
    {
        return Substitute.For<ITimeTickerManager<TimeTickerEntity>>();
    }

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
    public void ListJobs_ReturnsConfigured()
    {
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["nightly"] = new()
            {
                Type = "workflow",
                Workflow = "review-and-triage",
                Cron = "0 2 * * *",
                Enabled = true
            },
            ["hourly"] = new()
            {
                Type = "agent",
                Agent = "ticket-triager",
                Cron = "0 * * * *",
                Enabled = false
            }
        };

        var scheduler = CreateScheduler(jobs);
        var listed = scheduler.ListJobs();

        listed.Should().HaveCount(2);
        listed.Should().Contain(j => j.Name == "nightly" && j.IsEnabled);
        listed.Should().Contain(j => j.Name == "hourly" && !j.IsEnabled);
    }

    [Fact]
    public async Task TriggerJob_WorkflowType_EnqueuesViaTickerQ()
    {
        var timeManager = CreateTimeManager();

        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["test-job"] = new()
            {
                Type = "workflow",
                Workflow = "my-workflow",
                Cron = "0 0 * * *",
                Enabled = true
            }
        };

        var scheduler = CreateScheduler(jobs, timeManager: timeManager);

        // TriggerAsync may throw if TickerResult.IsSucceeded is false by default,
        // but we're testing that TriggerAsync calls through to the time manager
        try
        {
            await scheduler.TriggerAsync("test-job");
        }
        catch (InvalidOperationException)
        {
            // Expected if mock returns default TickerResult (IsSucceeded=false)
        }

        await timeManager.Received(1).AddAsync(Arg.Is<TimeTickerEntity>(e =>
            e.Function == "gantri_workflow_job"));
    }

    [Fact]
    public async Task TriggerJob_UnknownJob_Throws()
    {
        var scheduler = CreateScheduler(new Dictionary<string, ScheduledJobDefinition>());

        var act = () => scheduler.TriggerAsync("nonexistent");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void ListJobs_IncludesType()
    {
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["every-minute"] = new()
            {
                Type = "agent",
                Agent = "test",
                Cron = "* * * * *",
                Enabled = true
            }
        };

        var scheduler = CreateScheduler(jobs);
        var listed = scheduler.ListJobs();

        listed.Should().HaveCount(1);
        listed[0].Type.Should().Be("agent");
        listed[0].IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task PauseJob_MarksAsPaused()
    {
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["my-job"] = new()
            {
                Type = "workflow",
                Workflow = "test",
                Cron = "0 * * * *",
                Enabled = true
            }
        };

        var scheduler = CreateScheduler(jobs);

        await scheduler.PauseAsync("my-job");
        scheduler.ListJobs().Should().Contain(j => j.Name == "my-job" && j.IsPaused);
    }

    [Fact]
    public async Task PauseJob_UnknownJob_Throws()
    {
        var scheduler = CreateScheduler(new Dictionary<string, ScheduledJobDefinition>());

        var act = () => scheduler.PauseAsync("nonexistent");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GetJobDetail_ReturnsCorrectInfo()
    {
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["detail-job"] = new()
            {
                Type = "plugin",
                Plugin = "my-plugin",
                Action = "run",
                Cron = "0 0 * * *",
                Enabled = true
            }
        };

        var scheduler = CreateScheduler(jobs);
        var detail = await scheduler.GetJobDetailAsync("detail-job");

        detail.Should().NotBeNull();
        detail!.Name.Should().Be("detail-job");
        detail.Type.Should().Be("plugin");
    }

    [Fact]
    public async Task GetJobDetail_UnknownJob_ReturnsNull()
    {
        var scheduler = CreateScheduler(new Dictionary<string, ScheduledJobDefinition>());
        var detail = await scheduler.GetJobDetailAsync("nonexistent");
        detail.Should().BeNull();
    }

    [Fact]
    public void ListJobs_PluginType_Supported()
    {
        var jobs = new Dictionary<string, ScheduledJobDefinition>
        {
            ["plugin-job"] = new()
            {
                Type = "plugin",
                Plugin = "my-plugin",
                Action = "execute",
                Cron = "0 0 * * *",
                Enabled = true
            }
        };

        var scheduler = CreateScheduler(jobs);
        var listed = scheduler.ListJobs();

        listed.Should().HaveCount(1);
        listed[0].Type.Should().Be("plugin");
    }
}
