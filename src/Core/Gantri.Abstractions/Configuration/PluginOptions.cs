namespace Gantri.Abstractions.Configuration;

public sealed class PluginOptions
{
    public List<string> Dirs { get; set; } = [];
    public List<string> NativeTrustDirs { get; set; } = [];
    public List<PluginReference> Global { get; set; } = [];
    public Dictionary<string, List<PluginReference>> PerAgent { get; set; } = new();
}

public sealed class PluginReference
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = [];
}
