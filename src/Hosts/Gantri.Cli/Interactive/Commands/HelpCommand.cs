using Spectre.Console;

namespace Gantri.Cli.Interactive.Commands;

/// <summary>
/// /help — display available commands.
/// </summary>
internal sealed class HelpCommand : ISlashCommand
{
    private readonly SlashCommandRouter _router;

    public HelpCommand(SlashCommandRouter router)
    {
        _router = router;
    }

    public string Name => "help";
    public string Description => "Show available commands";

    public Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[green]Available Commands[/]");

        table.AddColumn("Command");
        table.AddColumn("Description");

        foreach (var (name, cmd) in _router.Commands.OrderBy(c => c.Key))
        {
            table.AddRow($"[cyan]/{Markup.Escape(name)}[/]", Markup.Escape(cmd.Description));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[dim]Tip: When an agent session is active, type directly to chat.[/]");
        return Task.CompletedTask;
    }
}
