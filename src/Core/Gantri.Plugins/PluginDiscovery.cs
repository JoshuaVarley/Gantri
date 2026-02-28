using System.Text.Json;
using Gantri.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace Gantri.Plugins;

public sealed class PluginDiscovery
{
    private readonly ILogger<PluginDiscovery> _logger;

    public PluginDiscovery(ILogger<PluginDiscovery> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<DiscoveredPlugin> ScanDirectories(IEnumerable<string> directories)
    {
        var discovered = new List<DiscoveredPlugin>();

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
            {
                _logger.LogWarning("Plugin directory does not exist, skipping: {Directory}", dir);
                continue;
            }

            foreach (var pluginDir in Directory.GetDirectories(dir))
            {
                var manifestPath = Path.Combine(pluginDir, "manifest.json");
                if (!File.Exists(manifestPath))
                    continue;

                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<PluginManifest>(json);
                    if (manifest is not null)
                    {
                        discovered.Add(new DiscoveredPlugin(pluginDir, manifest));
                        _logger.LogDebug("Discovered plugin '{Name}' ({Type}) at {Path}", manifest.Name, manifest.Type, pluginDir);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read plugin manifest at {Path}", manifestPath);
                }
            }
        }

        return discovered;
    }
}

public sealed record DiscoveredPlugin(string Path, PluginManifest Manifest);
