using System.ComponentModel;
using Gantri.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

internal sealed class ConfigValidateCommand : AsyncCommand<ConfigValidateCommand.Settings>
{
    private readonly YamlConfigurationLoader _loader;

    public ConfigValidateCommand(YamlConfigurationLoader loader)
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
            AnsiConsole.MarkupLine("[red]No configuration file found.[/]");
            AnsiConsole.MarkupLine(
                "[dim]Use --path to specify a config file, or create config/gantri.yaml[/]"
            );
            return Task.FromResult(1);
        }

        // Check file exists
        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[red]Configuration file not found:[/] {configPath}");
            return Task.FromResult(1);
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        // Try to parse
        GantriConfigRoot? config = null;
        try
        {
            config = _loader.LoadTypedWithImports<GantriConfigRoot>(configPath);
            AnsiConsole.MarkupLine("[green]YAML syntax:[/] Valid");
        }
        catch (Exception ex)
        {
            errors.Add($"YAML parse error: {ex.Message}");
        }

        if (config is not null)
        {
            // Validate providers
            foreach (var (providerName, providerOpts) in config.Ai.Providers)
            {
                if (string.IsNullOrWhiteSpace(providerOpts.ApiKey))
                    warnings.Add($"Provider '{providerName}' has no api_key configured");

                if (
                    string.IsNullOrWhiteSpace(providerOpts.Endpoint)
                    && string.IsNullOrWhiteSpace(providerOpts.BaseUrl)
                )
                    errors.Add($"Provider '{providerName}' has no endpoint or base_url configured");

                if (providerOpts.Models.Count == 0)
                    warnings.Add($"Provider '{providerName}' has no models defined");

                foreach (var (modelAlias, modelOpts) in providerOpts.Models)
                {
                    if (string.IsNullOrWhiteSpace(modelOpts.Id))
                        errors.Add($"Model '{modelAlias}' in provider '{providerName}' has no id");
                }
            }

            if (config.Ai.Providers.Count > 0)
                AnsiConsole.MarkupLine(
                    $"[green]Providers:[/] {config.Ai.Providers.Count} configured"
                );

            // Validate default model
            if (!string.IsNullOrWhiteSpace(config.Ai.DefaultModel))
            {
                var modelFound = config.Ai.Providers.Values.Any(p =>
                    p.Models.ContainsKey(config.Ai.DefaultModel)
                );
                if (!modelFound)
                    errors.Add(
                        $"Default model '{config.Ai.DefaultModel}' not found in any provider"
                    );
            }

            // Validate agents
            if (config.Agents.Count > 0)
            {
                foreach (var (name, def) in config.Agents)
                {
                    if (string.IsNullOrWhiteSpace(def.Model))
                    {
                        errors.Add($"Agent '{name}' has no model configured");
                        continue;
                    }

                    // Check provider reference
                    if (def.Provider is not null && !config.Ai.Providers.ContainsKey(def.Provider))
                        errors.Add($"Agent '{name}' references unknown provider '{def.Provider}'");

                    // Check model alias exists in the referenced provider (or any provider)
                    if (def.Provider is not null)
                    {
                        if (
                            config.Ai.Providers.TryGetValue(def.Provider, out var providerOpts)
                            && !providerOpts.Models.ContainsKey(def.Model)
                        )
                        {
                            errors.Add(
                                $"Agent '{name}' references model '{def.Model}' not found in provider '{def.Provider}'"
                            );
                        }
                    }
                    else
                    {
                        var modelFound = config.Ai.Providers.Values.Any(p =>
                            p.Models.ContainsKey(def.Model)
                        );
                        if (!modelFound)
                            errors.Add(
                                $"Agent '{name}' references model '{def.Model}' not found in any provider"
                            );
                    }
                }

                AnsiConsole.MarkupLine($"[green]Agents:[/] {config.Agents.Count} defined");
            }

            // Validate plugin directories
            foreach (var dir in config.Plugins.Dirs)
            {
                if (!Directory.Exists(dir))
                    warnings.Add($"Plugin directory not found: {dir}");
            }
        }

        // Report results
        if (warnings.Count > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{warnings.Count} warning(s):[/]");
            foreach (var warning in warnings)
                AnsiConsole.MarkupLine($"  [yellow]- {warning}[/]");
        }

        if (errors.Count == 0)
        {
            AnsiConsole.MarkupLine("[green bold]Configuration is valid.[/]");
            return Task.FromResult(0);
        }

        AnsiConsole.MarkupLine($"[red bold]{errors.Count} error(s):[/]");
        foreach (var error in errors)
            AnsiConsole.MarkupLine($"  [red]- {error}[/]");

        return Task.FromResult(1);
    }

    private static string? FindConfigPath()
    {
        var candidates = new[]
        {
            "config/gantri.yaml",
            "config/gantri.yml",
            "gantri.yaml",
            "gantri.yml",
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
