using Gantri.Abstractions.Workflows;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class WorkflowListCommand : AsyncCommand
{
    private readonly IWorkflowEngine _workflowEngine;

    public WorkflowListCommand(IWorkflowEngine workflowEngine)
    {
        _workflowEngine = workflowEngine;
    }

    public override Task<int> ExecuteAsync(CommandContext context)
    {
        var workflows = _workflowEngine.ListWorkflows();

        if (workflows.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No workflows configured.[/]");
            return Task.FromResult(0);
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Name")
            .AddColumn("Status");

        foreach (var name in workflows)
        {
            table.AddRow($"[bold]{name}[/]", "[green]Available[/]");
        }

        AnsiConsole.Write(table);
        return Task.FromResult(0);
    }
}
