namespace Gantri.Abstractions.Configuration;

public sealed class McpOptions
{
    public Dictionary<string, McpServerDefinition> Servers { get; set; } = new();
}

public sealed class McpServerDefinition
{
    public string Command { get; set; } = string.Empty;
    public List<string> Args { get; set; } = [];
    public Dictionary<string, string> Env { get; set; } = new();
    public string Transport { get; set; } = "stdio";
}
