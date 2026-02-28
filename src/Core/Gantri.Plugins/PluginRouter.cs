using Gantri.Abstractions.Plugins;
using Gantri.Telemetry;
using Microsoft.Extensions.Logging;

namespace Gantri.Plugins;

public sealed class PluginRouter : IPluginRouter
{
    private readonly IEnumerable<IPluginLoader> _loaders;
    private readonly PluginDiscovery _discovery;
    private readonly PluginManager _manager;
    private readonly ILogger<PluginRouter> _logger;
    private IReadOnlyList<DiscoveredPlugin>? _discoveredPlugins;

    public PluginRouter(
        IEnumerable<IPluginLoader> loaders,
        PluginDiscovery discovery,
        PluginManager manager,
        ILogger<PluginRouter> logger)
    {
        _loaders = loaders;
        _discovery = discovery;
        _manager = manager;
        _logger = logger;
    }

    public void ScanPluginDirectories(IEnumerable<string> directories)
    {
        _discoveredPlugins = _discovery.ScanDirectories(directories);
        _logger.LogInformation("Discovered {Count} plugins", _discoveredPlugins.Count);
    }

    public async Task<IPlugin> ResolveAsync(string pluginName, CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.Plugins.StartActivity("gantri.plugins.resolve");
        activity?.SetTag(GantriSemanticConventions.PluginName, pluginName);

        // Check if already loaded
        var existing = _manager.Get(pluginName);
        if (existing is not null)
            return existing;

        // Find in discovered plugins
        var discovered = _discoveredPlugins?.FirstOrDefault(p =>
            string.Equals(p.Manifest.Name, pluginName, StringComparison.OrdinalIgnoreCase));

        if (discovered is null)
            throw new InvalidOperationException($"Plugin '{pluginName}' not found. Run plugin discovery first.");

        // Find a loader that can handle this plugin type
        var loader = _loaders.FirstOrDefault(l => l.CanLoad(discovered.Manifest));
        if (loader is null)
        {
            var message = discovered.Manifest.Type == PluginType.Wasm
                ? $"WASM runtime not available. Plugin '{pluginName}' requires WASM support which is not currently registered. Install the Gantri.Plugins.Wasm package to enable WASM plugins."
                : $"No loader found for plugin '{pluginName}' of type '{discovered.Manifest.Type}'.";
            throw new InvalidOperationException(message);
        }

        var plugin = await loader.LoadAsync(discovered.Path, discovered.Manifest, cancellationToken);
        _manager.Register(plugin);
        return plugin;
    }

    public async Task<IReadOnlyList<IPlugin>> GetAllPluginsAsync(CancellationToken cancellationToken = default)
    {
        if (_discoveredPlugins is null)
            return _manager.LoadedPlugins;

        foreach (var discovered in _discoveredPlugins)
        {
            if (_manager.Get(discovered.Manifest.Name) is null)
            {
                try
                {
                    await ResolveAsync(discovered.Manifest.Name, cancellationToken);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("WASM runtime not available"))
                {
                    _logger.LogDebug("Skipping WASM plugin '{Name}': {Message}", discovered.Manifest.Name, ex.Message);
                }
            }
        }

        return _manager.LoadedPlugins;
    }

    public Task<IReadOnlyList<IPlugin>> GetPluginsByTypeAsync(PluginType type, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<IPlugin>>(_manager.GetByType(type));
    }

    public Task<IReadOnlyList<IPlugin>> GetPluginsByCapabilityAsync(PluginCapability capability, CancellationToken cancellationToken = default)
    {
        // Filter loaded plugins by capability declaration
        var matching = _manager.LoadedPlugins
            .Where(p => p.Manifest.Capabilities.Required.Any(c =>
                Enum.TryParse<PluginCapability>(c.Replace("-", ""), ignoreCase: true, out var cap) && capability.HasFlag(cap)))
            .ToList();

        return Task.FromResult<IReadOnlyList<IPlugin>>(matching);
    }
}
