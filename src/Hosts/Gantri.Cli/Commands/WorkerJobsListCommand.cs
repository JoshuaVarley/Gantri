using System.ComponentModel;
using Gantri.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

[Description("List all scheduled jobs from the worker")]
public sealed class WorkerJobsListCommand : AsyncCommand
{
    private readonly WorkerMcpClient _client;

    public WorkerJobsListCommand(WorkerMcpClient client)
    {
        _client = client;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            await _client.ConnectAsync();
            var result = await _client.CallToolAsync("scheduler_list_jobs");
            AnsiConsole.MarkupLine("[bold]Scheduled Jobs:[/]");
            AnsiConsole.WriteLine(result);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to list jobs: {ex.Message}[/]");
            return 1;
        }
    }
}
