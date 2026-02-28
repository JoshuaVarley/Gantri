namespace Gantri.Abstractions.Configuration;

public sealed class AgentDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public float? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public List<string> Skills { get; set; } = [];
    public List<string> McpServers { get; set; } = [];
    public List<string> AllowedActions { get; set; } = [];
    public List<string> Plugins { get; set; } = [];
    public string? SystemPromptFile { get; set; }
    public string? SystemPrompt { get; set; }
    public List<string> AllowedCommands { get; set; } = [];
    public string? WorkingDirectory { get; set; }
}
