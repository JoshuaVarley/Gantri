using System.ComponentModel;
using Gantri.Abstractions.Agents;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class AgentRunCommand : AsyncCommand<AgentRunCommand.Settings>
{
    private readonly IAgentOrchestrator _orchestrator;

    public AgentRunCommand(IAgentOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("The name of the agent to run")]
        public string AgentName { get; set; } = string.Empty;

        [CommandOption("--input|-i <TEXT>")]
        [Description("Input message to send to the agent")]
        public string? Input { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            await using var session = await _orchestrator.CreateSessionAsync(settings.AgentName);

            AnsiConsole.MarkupLine($"[green]Agent '[bold]{settings.AgentName}[/]' session started[/] (id: {session.SessionId})");
            AnsiConsole.WriteLine();

            if (settings.Input is not null)
            {
                // Single-shot mode: send input, print response, exit
                var response = await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Thinking...", async _ =>
                        await session.SendMessageAsync(settings.Input));

                AnsiConsole.MarkupLine("[blue]Assistant:[/]");
                AnsiConsole.WriteLine(response);
                return 0;
            }

            // Interactive mode
            AnsiConsole.MarkupLine("[dim]Type a message and press Enter. Type 'exit' to quit.[/]");
            AnsiConsole.WriteLine();

            while (true)
            {
                var input = AnsiConsole.Prompt(
                    new TextPrompt<string>("[yellow]You:[/]")
                        .AllowEmpty());

                if (string.IsNullOrWhiteSpace(input) ||
                    input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                    input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    AnsiConsole.MarkupLine("[dim]Session ended.[/]");
                    break;
                }

                var response = await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Thinking...", async _ =>
                        await session.SendMessageAsync(input));

                AnsiConsole.MarkupLine("[blue]Assistant:[/]");
                AnsiConsole.WriteLine(response);
                AnsiConsole.WriteLine();
            }

            return 0;
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
