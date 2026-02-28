using Gantri.Abstractions.Agents;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class AgentListCommand : AsyncCommand
{
    private readonly IAgentOrchestrator _orchestrator;

    public AgentListCommand(IAgentOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var agents = await _orchestrator.ListAgentsAsync();

        if (agents.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No agents configured.[/]");
            return 0;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Name")
            .AddColumn("Status");

        foreach (var agent in agents)
        {
            table.AddRow($"[bold]{agent}[/]", "[green]Available[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
