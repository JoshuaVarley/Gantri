using Gantri.Abstractions.Configuration;
using Gantri.Workflows;
using Spectre.Console;

namespace Gantri.Cli.Interactive;

/// <summary>
/// Interactive workflow approval step handler.
/// Instead of returning ApprovalPending and pausing the workflow, prompts the user inline.
/// </summary>
internal sealed class InteractiveApprovalStepHandler : IStepHandler
{
    private readonly ConsoleRenderer _renderer;

    public string StepType => "approval";

    public InteractiveApprovalStepHandler(ConsoleRenderer renderer)
    {
        _renderer = renderer;
    }

    public Task<StepResult> ExecuteAsync(
        WorkflowStepDefinition step,
        WorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var message = step.Input ?? $"Approval required for step '{step.Id}'";
        var resolvedMessage = context.ResolveTemplate(message);

        var panel = new Panel(Markup.Escape(resolvedMessage))
            .Header("[yellow]Workflow Approval Required[/]")
            .Border(BoxBorder.Rounded);
        AnsiConsole.Write(panel);

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Do you approve this step?[/]")
                .AddChoices("Approve", "Reject", "View step outputs"));

        if (choice == "View step outputs")
        {
            foreach (var (stepId, output) in context.StepOutputs)
            {
                AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(stepId)}:[/] {Markup.Escape(output?.ToString() ?? "(null)")}");
            }
            AnsiConsole.WriteLine();

            // Re-prompt after viewing
            choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Do you approve this step?[/]")
                    .AddChoices("Approve", "Reject"));
        }

        if (choice == "Approve")
        {
            _renderer.RenderSuccess($"Step '{step.Id}' approved.");
            return Task.FromResult(StepResult.Ok("approved"));
        }

        _renderer.RenderInfo($"Step '{step.Id}' rejected by user.");
        return Task.FromResult(StepResult.Fail("User rejected approval"));
    }
}
