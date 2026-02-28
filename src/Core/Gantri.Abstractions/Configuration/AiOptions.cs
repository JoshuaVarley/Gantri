namespace Gantri.Abstractions.Configuration;

public sealed class AiOptions
{
    public Dictionary<string, AiProviderOptions> Providers { get; set; } = new();
    public string DefaultModel { get; set; } = string.Empty;
}

public sealed class AiProviderOptions
{
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiVersion { get; set; }
    public Dictionary<string, AiModelOptions> Models { get; set; } = new();
}

public sealed class AiModelOptions
{
    public string Id { get; set; } = string.Empty;
    public string ApiType { get; set; } = "chat";
    public string? DeploymentName { get; set; }
    public string? Description { get; set; }
    public int MaxTokens { get; set; } = 4096;
    public float DefaultTemperature { get; set; } = 0.3f;
}