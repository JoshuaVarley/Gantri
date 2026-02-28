using Gantri.Abstractions.Scheduling;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class ScheduleListCommand : AsyncCommand
{
    private readonly IJobScheduler _scheduler;

    public ScheduleListCommand(IJobScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    public override Task<int> ExecuteAsync(CommandContext context)
    {
        var jobs = _scheduler.ListJobs();

        if (jobs.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No scheduled jobs configured.[/]");
            return Task.FromResult(0);
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Name")
            .AddColumn("Type")
            .AddColumn("Cron")
            .AddColumn("Enabled")
            .AddColumn("Next Run");

        foreach (var job in jobs)
        {
            table.AddRow(
                $"[bold]{job.Name}[/]",
                job.Type,
                job.CronExpression,
                job.IsEnabled ? "[green]Yes[/]" : "[red]No[/]",
                job.NextRun?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-");
        }

        AnsiConsole.Write(table);
        return Task.FromResult(0);
    }
}
