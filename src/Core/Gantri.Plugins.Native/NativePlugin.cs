using Gantri.Abstractions.Plugins;

namespace Gantri.Plugins.Native;

internal sealed class NativePlugin : IPlugin
{
    private readonly Dictionary<string, ReflectionPluginActionAdapter> _actions;
    private readonly NativePluginContext? _loadContext;

    public NativePlugin(
        PluginManifest manifest,
        Dictionary<string, ReflectionPluginActionAdapter> actions,
        NativePluginContext? loadContext)
    {
        Manifest = manifest;
        _actions = actions;
        _loadContext = loadContext;
    }

    public string Name => Manifest.Name;
    public string Version => Manifest.Version;
    public PluginType Type => PluginType.Native;
    public PluginManifest Manifest { get; }
    public IReadOnlyList<string> ActionNames => _actions.Keys.ToList();

    public async Task<PluginActionResult> ExecuteActionAsync(string actionName, PluginActionInput input, CancellationToken cancellationToken = default)
    {
        if (!_actions.TryGetValue(actionName, out var action))
            return PluginActionResult.Fail($"Action '{actionName}' not found in plugin '{Name}'.");

        return await action.ExecuteAsync(input, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _loadContext?.Unload();
        return ValueTask.CompletedTask;
    }
}
