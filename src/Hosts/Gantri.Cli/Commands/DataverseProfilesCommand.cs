using Gantri.Dataverse.Sdk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class DataverseProfilesCommand : AsyncCommand
{
    private readonly IDataverseConnectionProvider _provider;

    public DataverseProfilesCommand(IDataverseConnectionProvider provider)
    {
        _provider = provider;
    }

    public override Task<int> ExecuteAsync(CommandContext context)
    {
        var profiles = _provider.GetAvailableProfiles();
        var activeProfile = _provider.GetActiveProfile();

        if (profiles.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No Dataverse profiles configured. Add profiles to config/dataverse.yaml.[/]");
            return Task.FromResult(0);
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Active")
            .AddColumn("Profile Name")
            .AddColumn("Service Type");

        foreach (var profile in profiles)
        {
            var isActive = profile == activeProfile;
            table.AddRow(
                isActive ? "[green]\u25cf[/]" : "",
                isActive ? $"[bold]{profile}[/]" : profile,
                "dataverse");
        }

        AnsiConsole.Write(table);
        return Task.FromResult(0);
    }
}
