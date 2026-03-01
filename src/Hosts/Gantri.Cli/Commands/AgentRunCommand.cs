using System.ComponentModel;
using System.Diagnostics;
using Gantri.Abstractions.Agents;
using Gantri.Telemetry;
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
            // Root span for the entire conversation — all child operations nest under this
            using var conversationActivity = GantriActivitySources.Agents.StartActivity(
                "gantri.agent.conversation");
            var conversationId = Guid.NewGuid().ToString("N")[..12];
            var sw = Stopwatch.StartNew();

            conversationActivity?.SetTag(GantriSemanticConventions.AgentName, settings.AgentName);
            conversationActivity?.SetTag(GantriSemanticConventions.AgentConversationId, conversationId);
            conversationActivity?.SetTag(GantriSemanticConventions.GenAiConversationId, conversationId);
            conversationActivity?.SetTag(GantriSemanticConventions.GenAiAgentName, settings.AgentName);

            await using var session = await _orchestrator.CreateSessionAsync(settings.AgentName);
            conversationActivity?.SetTag(GantriSemanticConventions.AgentSessionId, session.SessionId);

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

                GantriMeters.AgentConversationDuration.Record(sw.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>(GantriSemanticConventions.AgentName, settings.AgentName));
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

            GantriMeters.AgentConversationDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(GantriSemanticConventions.AgentName, settings.AgentName));
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
