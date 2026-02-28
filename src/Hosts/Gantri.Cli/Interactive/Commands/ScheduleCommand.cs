using System.Text.Json;
using Gantri.Abstractions.Scheduling;
using Spectre.Console;

namespace Gantri.Cli.Interactive.Commands;

/// <summary>
/// /schedule [list|trigger|pause|resume|detail] — manage scheduled jobs via the Worker MCP server.
/// </summary>
internal sealed class ScheduleCommand : ISlashCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string Name => "schedule";
    public string Description => "Manage scheduled jobs (list, trigger, pause, resume, detail)";

    public async Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            await ListJobsAsync(context, ct);
            return;
        }

        var subcommand = args[0].ToLowerInvariant();

        switch (subcommand)
        {
            case "trigger":
                await TriggerJobAsync(args, context, ct);
                break;
            case "pause":
                await PauseJobAsync(args, context, ct);
                break;
            case "resume":
                await ResumeJobAsync(args, context, ct);
                break;
            case "detail":
                await ShowDetailAsync(args, context, ct);
                break;
            default:
                context.Renderer.RenderError(
                    "Usage: /schedule [list], /schedule trigger <name>, /schedule pause <name>, " +
                    "/schedule resume <name>, /schedule detail <name>");
                break;
        }
    }

    private static async Task ListJobsAsync(ConsoleContext context, CancellationToken ct)
    {
        try
        {
            List<ScheduledJobInfo>? jobs = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Connecting to worker...", async _ =>
                {
                    await context.WorkerClient.ConnectAsync(ct);
                    var json = await context.WorkerClient.CallToolAsync(
                        "scheduler_list_jobs", cancellationToken: ct);
                    jobs = JsonSerializer.Deserialize<List<ScheduledJobInfo>>(json, JsonOptions);
                });

            if (jobs is null || jobs.Count == 0)
            {
                context.Renderer.RenderInfo("No scheduled jobs found.");
                return;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[green]Scheduled Jobs[/]");
            table.AddColumn("Name");
            table.AddColumn("Type");
            table.AddColumn("Cron");
            table.AddColumn("Enabled");
            table.AddColumn("Paused");
            table.AddColumn("Next Run");

            foreach (var job in jobs)
            {
                table.AddRow(
                    Markup.Escape(job.Name),
                    Markup.Escape(job.Type),
                    Markup.Escape(job.CronExpression),
                    job.IsEnabled ? "[green]Yes[/]" : "[red]No[/]",
                    job.IsPaused ? "[yellow]Yes[/]" : "No",
                    job.NextRun?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-");
            }

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            context.Renderer.RenderError(
                $"Failed to connect to worker. Is the worker running? ({ex.Message})");
        }
    }

    private static async Task TriggerJobAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            context.Renderer.RenderError("Usage: /schedule trigger <job-name>");
            return;
        }

        var jobName = args[1];

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Triggering job '{jobName}'...", async _ =>
                {
                    await context.WorkerClient.ConnectAsync(ct);
                    await context.WorkerClient.CallToolAsync("scheduler_trigger_job",
                        new Dictionary<string, object?> { ["jobName"] = jobName }, ct);
                });

            context.Renderer.RenderSuccess($"Job '{jobName}' triggered.");
        }
        catch (Exception ex)
        {
            context.Renderer.RenderError(
                $"Failed to trigger job '{jobName}'. Is the worker running? ({ex.Message})");
        }
    }

    private static async Task PauseJobAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            context.Renderer.RenderError("Usage: /schedule pause <job-name>");
            return;
        }

        var jobName = args[1];

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Pausing job '{jobName}'...", async _ =>
                {
                    await context.WorkerClient.ConnectAsync(ct);
                    await context.WorkerClient.CallToolAsync("scheduler_pause_job",
                        new Dictionary<string, object?> { ["jobName"] = jobName }, ct);
                });

            context.Renderer.RenderSuccess($"Job '{jobName}' paused.");
        }
        catch (Exception ex)
        {
            context.Renderer.RenderError(
                $"Failed to pause job '{jobName}'. Is the worker running? ({ex.Message})");
        }
    }

    private static async Task ResumeJobAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            context.Renderer.RenderError("Usage: /schedule resume <job-name>");
            return;
        }

        var jobName = args[1];

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Resuming job '{jobName}'...", async _ =>
                {
                    await context.WorkerClient.ConnectAsync(ct);
                    await context.WorkerClient.CallToolAsync("scheduler_resume_job",
                        new Dictionary<string, object?> { ["jobName"] = jobName }, ct);
                });

            context.Renderer.RenderSuccess($"Job '{jobName}' resumed.");
        }
        catch (Exception ex)
        {
            context.Renderer.RenderError(
                $"Failed to resume job '{jobName}'. Is the worker running? ({ex.Message})");
        }
    }

    private static async Task ShowDetailAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            context.Renderer.RenderError("Usage: /schedule detail <job-name>");
            return;
        }

        var jobName = args[1];

        try
        {
            ScheduledJobDetail? detail = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching details for '{jobName}'...", async _ =>
                {
                    await context.WorkerClient.ConnectAsync(ct);
                    var json = await context.WorkerClient.CallToolAsync("scheduler_job_status",
                        new Dictionary<string, object?> { ["jobName"] = jobName }, ct);
                    detail = JsonSerializer.Deserialize<ScheduledJobDetail>(json, JsonOptions);
                });

            if (detail is null)
            {
                context.Renderer.RenderError($"Job '{jobName}' not found.");
                return;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"[green]Job: {Markup.Escape(detail.Name)}[/]");
            table.AddColumn("Property");
            table.AddColumn("Value");
            table.AddRow("Type", Markup.Escape(detail.Type));
            table.AddRow("Cron", Markup.Escape(detail.CronExpression));
            table.AddRow("Enabled", detail.IsEnabled ? "[green]Yes[/]" : "[red]No[/]");
            table.AddRow("Paused", detail.IsPaused ? "[yellow]Yes[/]" : "No");
            table.AddRow("Next Run", detail.NextRun?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-");
            table.AddRow("Last Run", detail.LastRun?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-");
            table.AddRow("Total Executions", detail.TotalExecutions.ToString());
            table.AddRow("Failed Executions", detail.FailedExecutions > 0
                ? $"[red]{detail.FailedExecutions}[/]"
                : "0");

            if (detail.LastError is not null)
                table.AddRow("Last Error", $"[red]{Markup.Escape(detail.LastError)}[/]");

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            context.Renderer.RenderError(
                $"Failed to get details for job '{jobName}'. Is the worker running? ({ex.Message})");
        }
    }
}
