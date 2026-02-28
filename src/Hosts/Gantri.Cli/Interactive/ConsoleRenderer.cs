using Gantri.Abstractions.Workflows;
using Spectre.Console;

namespace Gantri.Cli.Interactive;

/// <summary>
/// Handles all Spectre.Console rendering for the interactive console.
/// </summary>
internal sealed class ConsoleRenderer
{
    public void RenderWelcome()
    {
        AnsiConsole.Write(new FigletText("Gantri").Color(Color.Green));
        AnsiConsole.MarkupLine("[dim]Interactive Console v0.1.0[/]");
        AnsiConsole.MarkupLine("[dim]Type [green]/help[/] for available commands, or start chatting with an active agent.[/]");
        AnsiConsole.WriteLine();
    }

    public async Task RenderStreamingResponseAsync(IAsyncEnumerable<string> tokens, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[blue]Assistant:[/]");

        var fullResponse = new System.Text.StringBuilder();

        await foreach (var token in tokens.WithCancellation(cancellationToken))
        {
            fullResponse.Append(token);
            AnsiConsole.Write(token);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    public ToolApprovalChoice RenderToolApproval(string toolName, IReadOnlyDictionary<string, object?> parameters)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[yellow]Tool Call: {Markup.Escape(toolName)}[/]");

        table.AddColumn("Parameter");
        table.AddColumn("Value");

        foreach (var (key, value) in parameters)
        {
            // Skip framework-internal parameters (e.g., __allowed_commands)
            if (key.StartsWith("__"))
                continue;

            table.AddRow(
                Markup.Escape(key),
                Markup.Escape(FormatParameterValue(value)));
        }

        AnsiConsole.Write(table);

        return AnsiConsole.Prompt(
            new SelectionPrompt<ToolApprovalChoice>()
                .Title("[yellow]Allow this tool call?[/]")
                .AddChoices(ToolApprovalChoice.Approve, ToolApprovalChoice.Reject, ToolApprovalChoice.AlwaysApprove)
                .UseConverter(c => c switch
                {
                    ToolApprovalChoice.Approve => "Approve",
                    ToolApprovalChoice.Reject => "Reject",
                    ToolApprovalChoice.AlwaysApprove => "Always approve this tool",
                    _ => c.ToString()
                }));
    }

    public void RenderAgentList(IReadOnlyList<string> agents)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[green]Available Agents[/]");
        table.AddColumn("Name");

        foreach (var agent in agents)
            table.AddRow(Markup.Escape(agent));

        AnsiConsole.Write(table);
    }

    public void RenderWorkflowList(IReadOnlyList<string> workflows)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[green]Available Workflows[/]");
        table.AddColumn("Name");

        foreach (var wf in workflows)
            table.AddRow(Markup.Escape(wf));

        AnsiConsole.Write(table);
    }

    public void RenderWorkflowResult(WorkflowResult result)
    {
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]Workflow completed[/] in {result.Duration.TotalSeconds:F1}s");
            if (result.FinalOutput is not null)
            {
                var panel = new Panel(Markup.Escape(result.FinalOutput))
                    .Header("[blue]Output[/]")
                    .Border(BoxBorder.Rounded);
                AnsiConsole.Write(panel);
            }

            if (result.StepOutputs.Count > 0)
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Title("[dim]Step Outputs[/]");
                table.AddColumn("Step");
                table.AddColumn("Output");

                foreach (var (step, output) in result.StepOutputs)
                {
                    table.AddRow(
                        Markup.Escape(step),
                        Markup.Escape(output?.ToString()?.Length > 200
                            ? output.ToString()![..200] + "..."
                            : output?.ToString() ?? "(null)"));
                }

                AnsiConsole.Write(table);
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Workflow failed:[/] {Markup.Escape(result.Error ?? "Unknown error")}");
        }

        AnsiConsole.WriteLine();
    }

    public void RenderWorkflowStatus(WorkflowRunStatus status)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[green]Workflow: {Markup.Escape(status.WorkflowName)}[/]");
        table.AddColumn("Property");
        table.AddColumn("Value");
        table.AddRow("Execution ID", status.ExecutionId);
        table.AddRow("Status", status.Status);
        table.AddRow("Progress", $"{status.CompletedSteps}/{status.TotalSteps} steps");
        if (status.CurrentStep is not null)
            table.AddRow("Current Step", status.CurrentStep);
        if (status.Error is not null)
            table.AddRow("Error", status.Error);

        AnsiConsole.Write(table);
    }

    public void RenderActiveRuns(IReadOnlyList<WorkflowRunInfo> runs)
    {
        if (runs.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No active workflow runs.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[green]Active Workflow Runs[/]");
        table.AddColumn("ID");
        table.AddColumn("Workflow");
        table.AddColumn("Status");
        table.AddColumn("Progress");

        foreach (var run in runs)
        {
            table.AddRow(
                run.ExecutionId,
                run.WorkflowName,
                run.Status,
                $"{run.CompletedSteps}/{run.TotalSteps}");
        }

        AnsiConsole.Write(table);
    }

    private static string FormatParameterValue(object? value)
    {
        if (value is null)
            return "(null)";

        if (value is string s)
            return s;

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            var items = new List<string>();
            foreach (var item in enumerable)
                items.Add(item?.ToString() ?? "(null)");
            return $"[{string.Join(", ", items)}]";
        }

        return value.ToString() ?? "(null)";
    }

    public void RenderError(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
    }

    public void RenderInfo(string message)
    {
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(message)}[/]");
    }

    public void RenderSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");
    }
}

internal enum ToolApprovalChoice
{
    Approve,
    Reject,
    AlwaysApprove
}
