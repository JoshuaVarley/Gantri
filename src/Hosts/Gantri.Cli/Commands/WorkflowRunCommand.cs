using System.ComponentModel;
using Gantri.Abstractions.Workflows;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class WorkflowRunCommand : AsyncCommand<WorkflowRunCommand.Settings>
{
    private readonly IWorkflowEngine _workflowEngine;

    public WorkflowRunCommand(IWorkflowEngine workflowEngine)
    {
        _workflowEngine = workflowEngine;
    }

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("The name of the workflow to run")]
        public string WorkflowName { get; set; } = string.Empty;

        [CommandOption("--input|-i <TEXT>")]
        [Description("Input text to pass to the workflow")]
        public string? Input { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            var input = new Dictionary<string, object?>();
            if (settings.Input is not null)
                input["text"] = settings.Input;

            var result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Running workflow '{settings.WorkflowName}'...", async _ =>
                    await _workflowEngine.ExecuteAsync(settings.WorkflowName, input));

            if (result.Success)
            {
                AnsiConsole.MarkupLine($"[green]Workflow completed[/] in {result.Duration.TotalMilliseconds:F0}ms");
                AnsiConsole.WriteLine();

                foreach (var (stepId, output) in result.StepOutputs)
                {
                    AnsiConsole.MarkupLine($"[blue]{stepId}:[/]");
                    AnsiConsole.WriteLine(output?.ToString() ?? "(no output)");
                    AnsiConsole.WriteLine();
                }

                return 0;
            }

            AnsiConsole.MarkupLine($"[red]Workflow failed:[/] {result.Error}");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
