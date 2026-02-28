namespace Gantri.Abstractions.Plugins;

public interface IPlugin : IAsyncDisposable
{
    string Name { get; }
    string Version { get; }
    PluginType Type { get; }
    PluginManifest Manifest { get; }
    IReadOnlyList<string> ActionNames { get; }
    Task<PluginActionResult> ExecuteActionAsync(string actionName, PluginActionInput input, CancellationToken cancellationToken = default);
}

public sealed class PluginActionInput
{
    public string ActionName { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
    public string? WorkingDirectory { get; init; }
    public IPluginServices? Services { get; init; }
}

public sealed class PluginActionResult
{
    public bool Success { get; init; }
    public object? Output { get; init; }
    public string? Error { get; init; }

    public static PluginActionResult Ok(object? output = null) => new() { Success = true, Output = output };
    public static PluginActionResult Fail(string error) => new() { Success = false, Error = error };
}
