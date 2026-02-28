using Spectre.Console;

namespace Gantri.Cli.Interactive.Commands;

/// <summary>
/// /groupchat participants input — run a group chat with the specified agents.
/// </summary>
internal sealed class GroupChatInteractiveCommand : ISlashCommand
{
    public string Name => "groupchat";
    public string Description => "Run a group chat with multiple agents";

    public async Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            context.Renderer.RenderError("Usage: /groupchat <agent1,agent2,...> <input message>");
            return;
        }

        var participants = args[0]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (participants.Count < 2)
        {
            context.Renderer.RenderError("At least 2 participants are required for group chat.");
            return;
        }

        var input = string.Join(' ', args[1..]);

        try
        {
            AnsiConsole.MarkupLine($"[green]Starting group chat[/] with [bold]{Markup.Escape(string.Join(", ", participants))}[/]");

            var result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Running group chat...", async _ =>
                    await context.Orchestrator.RunGroupChatAsync(participants, input, cancellationToken: ct));

            AnsiConsole.MarkupLine("[blue]Group Chat Output:[/]");
            AnsiConsole.WriteLine(result);
            AnsiConsole.WriteLine();
        }
        catch (InvalidOperationException ex)
        {
            context.Renderer.RenderError(ex.Message);
        }
    }
}
