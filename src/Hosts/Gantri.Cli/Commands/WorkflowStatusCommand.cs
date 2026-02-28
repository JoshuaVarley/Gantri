using System.ComponentModel;
using Gantri.Abstractions.Workflows;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

[Description("Show status of a workflow execution")]
public sealed class WorkflowStatusCommand : AsyncCommand<WorkflowStatusCommand.Settings>
{
    private readonly IWorkflowEngine _workflowEngine;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<run-id>")]
        [Description("Workflow execution ID")]
        public string RunId { get; set; } = string.Empty;
    }

    public WorkflowStatusCommand(IWorkflowEngine workflowEngine)
    {
        _workflowEngine = workflowEngine;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var status = await _workflowEngine.GetRunStatusAsync(settings.RunId);
        if (status is null)
        {
            AnsiConsole.MarkupLine($"[yellow]No workflow run found with ID '{settings.RunId}'[/]");
            return 1;
        }

        var table = new Table();
        table.AddColumn("Property");
        table.AddColumn("Value");

        table.AddRow("Execution ID", status.ExecutionId);
        table.AddRow("Workflow", status.WorkflowName);
        table.AddRow("Status", status.Status);
        table.AddRow("Started", status.StartTime.ToString("o"));
        table.AddRow("Progress", $"{status.CompletedSteps}/{status.TotalSteps} steps");
        if (status.CurrentStep is not null)
            table.AddRow("Current Step", status.CurrentStep);
        if (status.Error is not null)
            table.AddRow("Error", status.Error);

        AnsiConsole.Write(table);
        return 0;
    }
}
