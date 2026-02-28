using System.ComponentModel;
using Gantri.Abstractions.Agents;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class GroupChatCommand : AsyncCommand<GroupChatCommand.Settings>
{
    private readonly IAgentOrchestrator _orchestrator;

    public GroupChatCommand(IAgentOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<participants>")]
        [Description("Comma-separated list of agent names to participate in the group chat")]
        public string Participants { get; set; } = string.Empty;

        [CommandOption("--input|-i <TEXT>")]
        [Description("Input message to start the group chat")]
        public string? Input { get; set; }

        [CommandOption("--max-iterations|-m <COUNT>")]
        [Description("Maximum number of group chat iterations")]
        [DefaultValue(5)]
        public int MaxIterations { get; set; } = 5;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            var participants = settings.Participants
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (participants.Count < 2)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] At least 2 participants are required for group chat.");
                return 1;
            }

            var input = settings.Input;
            if (string.IsNullOrWhiteSpace(input))
            {
                input = AnsiConsole.Prompt(
                    new TextPrompt<string>("[yellow]Input:[/]"));
            }

            AnsiConsole.MarkupLine($"[green]Starting group chat[/] with [bold]{string.Join(", ", participants)}[/]");
            AnsiConsole.MarkupLine($"[dim]Max iterations: {settings.MaxIterations}[/]");
            AnsiConsole.WriteLine();

            var result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Running group chat...", async _ =>
                    await _orchestrator.RunGroupChatAsync(participants, input, settings.MaxIterations));

            AnsiConsole.MarkupLine("[blue]Group Chat Output:[/]");
            AnsiConsole.WriteLine(result);

            return 0;
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
