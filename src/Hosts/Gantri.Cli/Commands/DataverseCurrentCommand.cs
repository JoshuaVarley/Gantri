using Gantri.Dataverse.Sdk;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class DataverseCurrentCommand : AsyncCommand
{
    private readonly IDataverseConnectionProvider _provider;

    public DataverseCurrentCommand(IDataverseConnectionProvider provider)
    {
        _provider = provider;
    }

    public override Task<int> ExecuteAsync(CommandContext context)
    {
        var activeProfile = _provider.GetActiveProfile();
        if (activeProfile is null)
        {
            AnsiConsole.MarkupLine("[yellow]No active Dataverse profile set.[/]");
            return Task.FromResult(0);
        }

        AnsiConsole.MarkupLine($"[bold]Active Profile:[/] {activeProfile}");
        AnsiConsole.MarkupLine($"[bold]Service Type:[/] dataverse");
        return Task.FromResult(0);
    }
}
