namespace Gantri.Cli.Interactive;

/// <summary>
/// Registry and dispatcher for slash commands in the interactive console.
/// </summary>
internal sealed class SlashCommandRouter
{
    private readonly Dictionary<string, ISlashCommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ISlashCommand command)
    {
        _commands[command.Name] = command;
    }

    public IReadOnlyDictionary<string, ISlashCommand> Commands => _commands;

    public bool TryGetCommand(string name, out ISlashCommand? command)
    {
        return _commands.TryGetValue(name, out command);
    }

    /// <summary>
    /// Parses a slash command input into name and arguments.
    /// Input "/agent news-summarizer" returns ("agent", ["news-summarizer"]).
    /// </summary>
    public static (string name, string[] args) Parse(string input)
    {
        var trimmed = input.TrimStart('/');
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return (string.Empty, []);

        return (parts[0], parts.Length > 1 ? parts[1..] : []);
    }
}
