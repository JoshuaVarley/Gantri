using System.Diagnostics;

namespace Gantri.Telemetry;

public static class GantriActivitySources
{
    public static readonly ActivitySource Hooks = new("Gantri.Hooks");
    public static readonly ActivitySource Plugins = new("Gantri.Plugins");
    public static readonly ActivitySource PluginsWasm = new("Gantri.Plugins.Wasm");
    public static readonly ActivitySource PluginsNative = new("Gantri.Plugins.Native");
    public static readonly ActivitySource AI = new("Gantri.AI");
    public static readonly ActivitySource Mcp = new("Gantri.Mcp");
    public static readonly ActivitySource Agents = new("Gantri.Agents");
    public static readonly ActivitySource Workflows = new("Gantri.Workflows");
    public static readonly ActivitySource Scheduling = new("Gantri.Scheduling");

    public static readonly IReadOnlyList<string> AllSourceNames =
    [
        "Gantri.Hooks",
        "Gantri.Plugins",
        "Gantri.Plugins.Wasm",
        "Gantri.Plugins.Native",
        "Gantri.AI",
        "Gantri.Mcp",
        "Gantri.Agents",
        "Gantri.Workflows",
        "Gantri.Scheduling"
    ];
}
