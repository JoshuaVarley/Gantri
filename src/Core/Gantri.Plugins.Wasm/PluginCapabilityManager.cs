using Gantri.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace Gantri.Plugins.Wasm;

/// <summary>
/// Resolves manifest capabilities to PluginCapability flags.
/// Fails plugin load if a required capability is denied.
/// </summary>
public sealed class PluginCapabilityManager
{
    private readonly ILogger<PluginCapabilityManager> _logger;

    private static readonly Dictionary<string, PluginCapability> CapabilityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["log"] = PluginCapability.Log,
        ["config_read"] = PluginCapability.ConfigRead,
        ["ai_complete"] = PluginCapability.AiComplete,
        ["fs_read"] = PluginCapability.FsRead,
        ["fs_write"] = PluginCapability.FsWrite,
        ["http_request"] = PluginCapability.HttpRequest,
        ["mcp_call"] = PluginCapability.McpCall,
    };

    public PluginCapabilityManager(ILogger<PluginCapabilityManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolves a plugin manifest's capabilities to flags.
    /// Log is always granted. Required capabilities that aren't recognized cause a load failure.
    /// </summary>
    public PluginCapability ResolveCapabilities(PluginManifest manifest)
    {
        var granted = PluginCapability.Log; // Always granted

        foreach (var cap in manifest.Capabilities.Required)
        {
            if (CapabilityMap.TryGetValue(cap, out var flag))
            {
                granted |= flag;
                _logger.LogDebug("Granted required capability '{Capability}' to plugin '{Plugin}'", cap, manifest.Name);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Plugin '{manifest.Name}' requires unknown capability '{cap}'. Cannot load.");
            }
        }

        foreach (var cap in manifest.Capabilities.Optional)
        {
            if (CapabilityMap.TryGetValue(cap, out var flag))
            {
                granted |= flag;
                _logger.LogDebug("Granted optional capability '{Capability}' to plugin '{Plugin}'", cap, manifest.Name);
            }
            else
            {
                _logger.LogWarning("Plugin '{Plugin}' requests unknown optional capability '{Capability}' — skipping",
                    manifest.Name, cap);
            }
        }

        return granted;
    }
}
