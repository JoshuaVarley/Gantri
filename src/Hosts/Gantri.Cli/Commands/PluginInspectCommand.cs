using System.ComponentModel;
using Gantri.Abstractions.Plugins;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

[Description("Show detailed information about a plugin")]
public sealed class PluginInspectCommand : AsyncCommand<PluginInspectCommand.Settings>
{
    private readonly IPluginRouter _pluginRouter;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("Name of the plugin to inspect")]
        public string Name { get; set; } = string.Empty;
    }

    public PluginInspectCommand(IPluginRouter pluginRouter)
    {
        _pluginRouter = pluginRouter;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            var plugin = await _pluginRouter.ResolveAsync(settings.Name);

            var table = new Table();
            table.AddColumn("Property");
            table.AddColumn("Value");

            table.AddRow("Name", plugin.Name);
            table.AddRow("Version", plugin.Version);
            table.AddRow("Type", plugin.Type.ToString());
            table.AddRow("Description", plugin.Manifest.Description);
            table.AddRow("Entry", plugin.Manifest.Entry);
            table.AddRow("Trust", plugin.Manifest.Trust ?? "default");

            if (plugin.Manifest.Capabilities.Required.Count > 0)
                table.AddRow("Required Capabilities", string.Join(", ", plugin.Manifest.Capabilities.Required));
            if (plugin.Manifest.Capabilities.Optional.Count > 0)
                table.AddRow("Optional Capabilities", string.Join(", ", plugin.Manifest.Capabilities.Optional));

            AnsiConsole.Write(table);

            if (plugin.ActionNames.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Actions:[/]");
                var actionsTable = new Table();
                actionsTable.AddColumn("Name");
                actionsTable.AddColumn("Description");

                foreach (var action in plugin.Manifest.Exports.Actions)
                    actionsTable.AddRow(action.Name, action.Description);

                AnsiConsole.Write(actionsTable);
            }

            if (plugin.Manifest.Exports.Hooks.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Hooks:[/]");
                var hooksTable = new Table();
                hooksTable.AddColumn("Event");
                hooksTable.AddColumn("Function");

                foreach (var hook in plugin.Manifest.Exports.Hooks)
                    hooksTable.AddRow(hook.Event, hook.Function);

                AnsiConsole.Write(hooksTable);
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Plugin '{settings.Name}' not found: {ex.Message}[/]");
            return 1;
        }
    }
}
