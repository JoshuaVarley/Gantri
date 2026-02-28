namespace Gantri.Abstractions.Plugins;

public interface IPluginLoader
{
    PluginType SupportedType { get; }
    Task<IPlugin> LoadAsync(string pluginPath, PluginManifest manifest, CancellationToken cancellationToken = default);
    Task UnloadAsync(string pluginName, CancellationToken cancellationToken = default);
    bool CanLoad(PluginManifest manifest);
}
