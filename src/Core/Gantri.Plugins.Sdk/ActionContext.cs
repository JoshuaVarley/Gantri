using Gantri.Abstractions.Plugins;

namespace Gantri.Plugins.Sdk;

public sealed class ActionContext
{
    public string ActionName { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
    public Gantri.Abstractions.Plugins.IPluginServices? Services { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public string? WorkingDirectory { get; init; }
}
