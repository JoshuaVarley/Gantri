using Spectre.Console;

namespace Gantri.Cli.Interactive.Commands;

/// <summary>
/// /approve [id] — resume a paused workflow by approving it.
/// </summary>
internal sealed class ApproveCommand : ISlashCommand
{
    public string Name => "approve";
    public string Description => "Approve a pending workflow execution";

    public async Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            // Try to find a pending approval
            var runs = await context.WorkflowEngine.ListActiveRunsAsync(ct);
            var pending = runs.Where(r => r.Status == "waiting_approval").ToList();

            if (pending.Count == 0)
            {
                context.Renderer.RenderInfo("No workflows waiting for approval.");
                return;
            }

            if (pending.Count == 1)
            {
                await ResumeWorkflow(pending[0].ExecutionId, context, ct);
                return;
            }

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Which workflow to approve?[/]")
                    .AddChoices(pending.Select(r => $"{r.ExecutionId} ({r.WorkflowName})")));

            var executionId = selected.Split(' ')[0];
            await ResumeWorkflow(executionId, context, ct);
            return;
        }

        await ResumeWorkflow(args[0], context, ct);
    }

    private static async Task ResumeWorkflow(string executionId, ConsoleContext context, CancellationToken ct)
    {
        try
        {
            var result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Resuming workflow '{executionId}'...", async _ =>
                    await context.WorkflowEngine.ResumeAsync(executionId, ct));

            context.Renderer.RenderWorkflowResult(result);
        }
        catch (InvalidOperationException ex)
        {
            context.Renderer.RenderError(ex.Message);
        }
    }
}
