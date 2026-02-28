namespace Gantri.Abstractions.AI;

public sealed class ModelConfiguration
{
    public string Alias { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MaxTokens { get; set; } = 4096;
    public float DefaultTemperature { get; set; } = 0.3f;
}
