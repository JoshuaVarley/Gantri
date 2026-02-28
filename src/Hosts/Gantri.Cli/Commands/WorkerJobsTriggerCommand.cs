using System.ComponentModel;
using Gantri.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

[Description("Manually trigger a scheduled job")]
public sealed class WorkerJobsTriggerCommand : AsyncCommand<WorkerJobsTriggerCommand.Settings>
{
    private readonly WorkerMcpClient _client;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("Name of the job to trigger")]
        public string Name { get; set; } = string.Empty;
    }

    public WorkerJobsTriggerCommand(WorkerMcpClient client)
    {
        _client = client;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            await _client.ConnectAsync();
            var result = await _client.CallToolAsync("scheduler_trigger_job",
                new Dictionary<string, object?> { ["jobName"] = settings.Name });
            AnsiConsole.MarkupLine($"[green]Job triggered:[/] {result}");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to trigger job: {ex.Message}[/]");
            return 1;
        }
    }
}
