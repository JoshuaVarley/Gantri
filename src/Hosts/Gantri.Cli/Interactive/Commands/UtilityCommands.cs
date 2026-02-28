using Spectre.Console;

namespace Gantri.Cli.Interactive.Commands;

/// <summary>
/// /tools — list tools available to the current agent session.
/// </summary>
internal sealed class ToolsCommand : ISlashCommand
{
    public string Name => "tools";
    public string Description => "List tools available to current agent";

    public Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        if (context.ActiveSession is null)
        {
            context.Renderer.RenderError("No active agent session. Use /agent <name> first.");
            return Task.CompletedTask;
        }

        context.Renderer.RenderInfo($"Agent '{context.ActiveAgentName}' is active. Tools are resolved at agent creation time.");
        context.Renderer.RenderInfo("Use /agent list to see available agents.");
        return Task.CompletedTask;
    }
}

/// <summary>
/// /session — show current session info.
/// </summary>
internal sealed class SessionCommand : ISlashCommand
{
    public string Name => "session";
    public string Description => "Show current session info";

    public Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        if (context.ActiveSession is null)
        {
            context.Renderer.RenderInfo("No active session. Use /agent <name> to start one.");
            return Task.CompletedTask;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[green]Current Session[/]");
        table.AddColumn("Property");
        table.AddColumn("Value");
        table.AddRow("Agent", context.ActiveAgentName ?? "unknown");
        table.AddRow("Session ID", context.ActiveSession.SessionId);
        table.AddRow("Messages", context.MessageCount.ToString());

        AnsiConsole.Write(table);
        return Task.CompletedTask;
    }
}

/// <summary>
/// /clear — clear the console.
/// </summary>
internal sealed class ClearCommand : ISlashCommand
{
    public string Name => "clear";
    public string Description => "Clear the console";

    public Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        AnsiConsole.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>
/// /exit — exit the interactive console.
/// </summary>
internal sealed class ExitCommand : ISlashCommand
{
    public string Name => "exit";
    public string Description => "Exit the console";

    public Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        context.ExitRequested = true;
        return Task.CompletedTask;
    }
}

/// <summary>
/// /quit — alias for /exit.
/// </summary>
internal sealed class QuitCommand : ISlashCommand
{
    public string Name => "quit";
    public string Description => "Exit the console";

    public Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct)
    {
        context.ExitRequested = true;
        return Task.CompletedTask;
    }
}
