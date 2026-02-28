namespace Gantri.Plugins.Sdk;

public sealed class ActionContext
{
    public string ActionName { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
    public IPluginServices? Services { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public string? WorkingDirectory { get; init; }
}
