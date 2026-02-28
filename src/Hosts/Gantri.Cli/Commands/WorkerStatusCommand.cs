using System.ComponentModel;
using Gantri.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

[Description("Show worker health status")]
public sealed class WorkerStatusCommand : AsyncCommand
{
    private readonly WorkerMcpClient _client;

    public WorkerStatusCommand(WorkerMcpClient client)
    {
        _client = client;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            await _client.ConnectAsync();
            var result = await _client.CallToolAsync("worker_status");
            AnsiConsole.MarkupLine("[bold]Worker Status:[/]");
            AnsiConsole.WriteLine(result);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to connect to worker: {ex.Message}[/]");
            return 1;
        }
    }
}
