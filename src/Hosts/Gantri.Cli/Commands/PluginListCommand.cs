using Gantri.Abstractions.Plugins;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class PluginListCommand : AsyncCommand
{
    private readonly IPluginRouter _pluginRouter;

    public PluginListCommand(IPluginRouter pluginRouter)
    {
        _pluginRouter = pluginRouter;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var plugins = await _pluginRouter.GetAllPluginsAsync();

        if (plugins.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No plugins found.[/]");
            return 0;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Name")
            .AddColumn("Version")
            .AddColumn("Type")
            .AddColumn("Actions");

        foreach (var plugin in plugins)
        {
            table.AddRow(
                $"[bold]{plugin.Name}[/]",
                plugin.Version,
                plugin.Type.ToString(),
                string.Join(", ", plugin.ActionNames));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
