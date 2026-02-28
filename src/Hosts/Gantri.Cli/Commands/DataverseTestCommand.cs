using System.ComponentModel;
using Gantri.Dataverse.Sdk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class DataverseTestCommand : AsyncCommand<DataverseTestCommand.Settings>
{
    private readonly IDataverseConnectionProvider _provider;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[name]")]
        [Description("Profile name to test (uses active profile if omitted)")]
        public string? Name { get; set; }
    }

    public DataverseTestCommand(IDataverseConnectionProvider provider)
    {
        _provider = provider;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var profileName = settings.Name ?? _provider.GetActiveProfile();
        if (profileName is null)
        {
            AnsiConsole.MarkupLine("[red]No profile specified and no active profile set.[/]");
            return 1;
        }

        var success = await AnsiConsole.Status()
            .StartAsync($"Testing connection to '{profileName}'...", async _ =>
                await _provider.TestConnectionAsync(profileName));

        if (success)
            AnsiConsole.MarkupLine($"[green]Connection to '{profileName}' succeeded.[/]");
        else
            AnsiConsole.MarkupLine($"[red]Connection to '{profileName}' failed.[/]");

        return success ? 0 : 1;
    }
}
