namespace Gantri.Cli.Interactive;

/// <summary>
/// Interface for interactive console slash commands.
/// </summary>
internal interface ISlashCommand
{
    string Name { get; }
    string Description { get; }
    Task ExecuteAsync(string[] args, ConsoleContext context, CancellationToken ct);
}
