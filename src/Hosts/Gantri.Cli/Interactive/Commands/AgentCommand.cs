using Spectre.Console;

namespace Gantri.Cli.Interactive.Commands;

/// <summary>
/// /agent [name|list] — start interactive session with a named agent or list agents.
/// </summary>
internal sealed class AgentCommand : ISlashCommand
{
    public string Name => "agent";
    public string Description => "Start an agent session or list agents";

    public async Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            context.Renderer.RenderError("Usage: /agent <name> or /agent list");
            return;
        }

        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            var agents = await context.Orchestrator.ListAgentsAsync(ct);
            context.Renderer.RenderAgentList(agents);
            return;
        }

        var agentName = args[0];

        // End any existing session
        if (context.ActiveSession is not null)
        {
            context.Renderer.RenderInfo($"Ending session with '{context.ActiveAgentName}'...");
            await context.EndSessionAsync();
        }

        try
        {
            var session = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Starting session with '{agentName}'...", async _ =>
                    await context.Orchestrator.CreateSessionAsync(agentName, ct));

            context.ActiveSession = session;
            context.ActiveAgentName = agentName;
            context.MessageCount = 0;

            context.Renderer.RenderSuccess($"Session started with '{agentName}' (id: {session.SessionId})");
            AnsiConsole.MarkupLine("[dim]Send messages directly or use /exit to end the session.[/]");
        }
        catch (InvalidOperationException ex)
        {
            context.Renderer.RenderError(ex.Message);
        }
    }
}
