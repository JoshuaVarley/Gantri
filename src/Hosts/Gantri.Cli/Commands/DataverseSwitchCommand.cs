using System.ComponentModel;
using Gantri.Dataverse.Sdk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class DataverseSwitchCommand : AsyncCommand<DataverseSwitchCommand.Settings>
{
    private readonly IDataverseConnectionProvider _provider;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("Name of the profile to switch to")]
        public string Name { get; set; } = string.Empty;
    }

    public DataverseSwitchCommand(IDataverseConnectionProvider provider)
    {
        _provider = provider;
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            _provider.SetActiveProfile(settings.Name);
            AnsiConsole.MarkupLine($"[green]Active Dataverse profile switched to '{settings.Name}'.[/]");
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to switch profile: {ex.Message}[/]");
            return Task.FromResult(1);
        }
    }
}
