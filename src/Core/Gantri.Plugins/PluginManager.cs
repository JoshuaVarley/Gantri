using System.Collections.Concurrent;
using Gantri.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace Gantri.Plugins;

public sealed class PluginManager
{
    private readonly ConcurrentDictionary<string, IPlugin> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<PluginManager> _logger;

    public PluginManager(ILogger<PluginManager> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<IPlugin> LoadedPlugins => _loadedPlugins.Values.ToList();

    public void Register(IPlugin plugin)
    {
        _loadedPlugins[plugin.Name] = plugin;
        _logger.LogInformation("Registered plugin '{Name}' v{Version} ({Type})", plugin.Name, plugin.Version, plugin.Type);
    }

    public IPlugin? Get(string name)
    {
        _loadedPlugins.TryGetValue(name, out var plugin);
        return plugin;
    }

    public IReadOnlyList<IPlugin> GetByType(PluginType type)
    {
        return _loadedPlugins.Values.Where(p => p.Type == type).ToList();
    }

    public async Task UnloadAsync(string name)
    {
        if (_loadedPlugins.TryRemove(name, out var plugin))
        {
            await plugin.DisposeAsync();
            _logger.LogInformation("Unloaded plugin '{Name}'", name);
        }
    }
}
