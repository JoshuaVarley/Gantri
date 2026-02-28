using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gantri.Abstractions.Plugins;

public sealed class PluginManifest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter<PluginType>))]
    public PluginType Type { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("entry")]
    public string Entry { get; set; } = string.Empty;

    [JsonPropertyName("trust")]
    public string? Trust { get; set; }

    [JsonPropertyName("capabilities")]
    public PluginCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("exports")]
    public PluginExports Exports { get; set; } = new();
}

public sealed class PluginCapabilities
{
    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = [];

    [JsonPropertyName("optional")]
    public List<string> Optional { get; set; } = [];
}

public sealed class PluginExports
{
    [JsonPropertyName("actions")]
    public List<PluginActionExport> Actions { get; set; } = [];

    [JsonPropertyName("hooks")]
    public List<PluginHookExport> Hooks { get; set; } = [];
}

public sealed class PluginActionExport
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; set; }
}

public sealed class PluginHookExport
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("function")]
    public string Function { get; set; } = string.Empty;
}
