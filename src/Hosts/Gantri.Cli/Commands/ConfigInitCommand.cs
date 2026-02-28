using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Gantri.Cli.Commands;

[Description("Scaffold a split config directory")]
public sealed class ConfigInitCommand : Command<ConfigInitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--path <PATH>")]
        [Description("Output directory for config files")]
        public string Path { get; set; } = "config";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var dir = settings.Path;

        if (Directory.Exists(dir) && Directory.GetFiles(dir).Length > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Config directory already exists and contains files.[/]");
            if (!AnsiConsole.Confirm("Overwrite existing configuration?", false))
                return 0;
        }

        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(System.IO.Path.Combine(dir, "agents"));
        Directory.CreateDirectory(System.IO.Path.Combine(dir, "workflows"));

        File.WriteAllText(System.IO.Path.Combine(dir, "gantri.yaml"), """
            framework:
              name: Gantri
              version: 0.1.0
              log_level: Information
              data_dir: ./data
              imports:
                - ai.yaml
                - agents.yaml
                - plugins.yaml
                - telemetry.yaml
                - scheduling.yaml
                - worker.yaml
                - agents/*.yaml
                - workflows/*.yaml
            """.Replace("            ", ""));

        File.WriteAllText(System.IO.Path.Combine(dir, "ai.yaml"), """
            ai:
              default_model: gpt-4o-mini
              providers: {}
            """.Replace("            ", ""));

        File.WriteAllText(System.IO.Path.Combine(dir, "agents.yaml"), """
            agents: {}
            """.Replace("            ", ""));

        File.WriteAllText(System.IO.Path.Combine(dir, "plugins.yaml"), """
            plugins:
              dirs:
                - ./plugins/built-in
            """.Replace("            ", ""));

        File.WriteAllText(System.IO.Path.Combine(dir, "telemetry.yaml"), """
            telemetry:
              enabled: true
              service_name: gantri
            """.Replace("            ", ""));

        File.WriteAllText(System.IO.Path.Combine(dir, "scheduling.yaml"), """
            scheduling:
              storage:
                provider: sqlite
                connection_string: "Data Source=data/gantri-scheduling.db"
              jobs: {}
            """.Replace("            ", ""));

        File.WriteAllText(System.IO.Path.Combine(dir, "worker.yaml"), """
            worker:
              mcp:
                transport: stdio
                host: localhost
                port: 5100
                auth:
                  type: none
            """.Replace("            ", ""));

        AnsiConsole.MarkupLine($"[green]Config directory scaffolded at '{dir}'[/]");
        return 0;
    }
}
