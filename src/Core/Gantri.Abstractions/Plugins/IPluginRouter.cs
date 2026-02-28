namespace Gantri.Abstractions.Plugins;

public interface IPluginRouter
{
    Task<IPlugin> ResolveAsync(string pluginName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IPlugin>> GetAllPluginsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IPlugin>> GetPluginsByTypeAsync(PluginType type, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IPlugin>> GetPluginsByCapabilityAsync(PluginCapability capability, CancellationToken cancellationToken = default);
}
