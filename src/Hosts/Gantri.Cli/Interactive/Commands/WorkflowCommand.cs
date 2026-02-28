using Spectre.Console;

namespace Gantri.Cli.Interactive.Commands;

/// <summary>
/// /workflow [name|list|status] — run, list, or check status of workflows.
/// </summary>
internal sealed class WorkflowCommand : ISlashCommand
{
    public string Name => "workflow";
    public string Description => "Run a workflow, list workflows, or check status";

    public async Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            context.Renderer.RenderError("Usage: /workflow <name>, /workflow list, or /workflow status [id]");
            return;
        }

        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            var workflows = context.WorkflowEngine.ListWorkflows();
            context.Renderer.RenderWorkflowList(workflows);
            return;
        }

        if (args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 1)
            {
                var status = await context.WorkflowEngine.GetRunStatusAsync(args[1], ct);
                if (status is not null)
                    context.Renderer.RenderWorkflowStatus(status);
                else
                    context.Renderer.RenderError($"No workflow run found with id '{args[1]}'.");
            }
            else
            {
                var runs = await context.WorkflowEngine.ListActiveRunsAsync(ct);
                context.Renderer.RenderActiveRuns(runs);
            }
            return;
        }

        // Run workflow by name
        var workflowName = args[0];

        try
        {
            var result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Running workflow '{workflowName}'...", async _ =>
                    await context.WorkflowEngine.ExecuteAsync(workflowName, cancellationToken: ct));

            context.Renderer.RenderWorkflowResult(result);
        }
        catch (InvalidOperationException ex)
        {
            context.Renderer.RenderError(ex.Message);
        }
    }
}
