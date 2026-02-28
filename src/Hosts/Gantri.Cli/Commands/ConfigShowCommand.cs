using System.ComponentModel;
using Gantri.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class ConfigShowCommand : AsyncCommand<ConfigShowCommand.Settings>
{
    private readonly YamlConfigurationLoader _loader;

    public ConfigShowCommand(YamlConfigurationLoader loader)
    {
        _loader = loader;
    }

    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--path|-p <PATH>")]
        [Description("Path to the configuration file")]
        public string? ConfigPath { get; set; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var configPath = settings.ConfigPath ?? FindConfigPath();

        if (configPath is null)
        {
            AnsiConsole.MarkupLine("[yellow]No configuration file found.[/]");
            AnsiConsole.MarkupLine("[dim]Use --path to specify a config file, or create config/gantri.yaml[/]");
            return Task.FromResult(1);
        }

        try
        {
            var config = _loader.Load<GantriConfigRoot>(configPath);
            var tree = new Tree($"[bold]Configuration[/] [dim]({configPath})[/]");

            // Framework section
            var framework = tree.AddNode("[blue]Framework[/]");
            framework.AddNode($"Name: {config.Framework.Name}");
            framework.AddNode($"Version: {config.Framework.Version}");
            framework.AddNode($"Log Level: {config.Framework.LogLevel}");

            // AI section
            var ai = tree.AddNode("[blue]AI[/]");
            ai.AddNode($"Default Model: {(string.IsNullOrEmpty(config.Ai.DefaultModel) ? "(not set)" : config.Ai.DefaultModel)}");
            if (config.Ai.Providers.Count > 0)
            {
                var providers = ai.AddNode("Providers");
                foreach (var (name, _) in config.Ai.Providers)
                    providers.AddNode(name);
            }

            // Agents section
            var agents = tree.AddNode("[blue]Agents[/]");
            if (config.Agents.Count == 0)
            {
                agents.AddNode("[dim]None configured[/]");
            }
            else
            {
                foreach (var (name, def) in config.Agents)
                {
                    var agentNode = agents.AddNode($"[bold]{name}[/]");
                    agentNode.AddNode($"Model: {def.Model}");
                    if (def.Provider is not null)
                        agentNode.AddNode($"Provider: {def.Provider}");
                }
            }

            // Plugins section
            var plugins = tree.AddNode("[blue]Plugins[/]");
            if (config.Plugins.Dirs.Count == 0)
            {
                plugins.AddNode("[dim]No directories configured[/]");
            }
            else
            {
                foreach (var dir in config.Plugins.Dirs)
                    plugins.AddNode(dir);
            }

            AnsiConsole.Write(tree);
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error loading config:[/] {ex.Message}");
            return Task.FromResult(1);
        }
    }

    private static string? FindConfigPath()
    {
        var candidates = new[] { "config/gantri.yaml", "config/gantri.yml", "gantri.yaml", "gantri.yml" };
        return candidates.FirstOrDefault(File.Exists);
    }
}
