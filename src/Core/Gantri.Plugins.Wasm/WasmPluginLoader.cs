using Gantri.Abstractions.Plugins;
using Gantri.Telemetry;
using Microsoft.Extensions.Logging;
using Wasmtime;

namespace Gantri.Plugins.Wasm;

public sealed class WasmPluginLoader : IPluginLoader
{
    private readonly WasmPluginHost _host;
    private readonly HostFunctionRegistry _hostFunctionRegistry;
    private readonly PluginCapabilityManager _capabilityManager;
    private readonly ILogger<WasmPluginLoader> _logger;
    private readonly Dictionary<string, WasmPlugin> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);

    public PluginType SupportedType => PluginType.Wasm;

    public WasmPluginLoader(
        WasmPluginHost host,
        HostFunctionRegistry hostFunctionRegistry,
        PluginCapabilityManager capabilityManager,
        ILogger<WasmPluginLoader> logger)
    {
        _host = host;
        _hostFunctionRegistry = hostFunctionRegistry;
        _capabilityManager = capabilityManager;
        _logger = logger;
    }

    public bool CanLoad(PluginManifest manifest) => manifest.Type == PluginType.Wasm;

    public Task<IPlugin> LoadAsync(string pluginPath, PluginManifest manifest, CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.PluginsWasm.StartActivity("gantri.plugins.wasm.load");
        activity?.SetTag(GantriSemanticConventions.PluginName, manifest.Name);

        var wasmPath = Path.Combine(pluginPath, manifest.Entry);
        if (!File.Exists(wasmPath))
            throw new FileNotFoundException($"WASM module not found: {wasmPath}");

        // Resolve capabilities from manifest
        var grantedCapabilities = _capabilityManager.ResolveCapabilities(manifest);

        var module = _host.LoadModule(wasmPath);
        var store = new Store(_host.Engine);

        // Set fuel budget and memory limits
        store.Fuel = (ulong)_host.DefaultFuelBudget;
        store.SetLimits(
            memorySize: _host.DefaultMemoryLimit,
            tableElements: 10_000,
            instances: 10,
            tables: 10,
            memories: 1);

        var linker = new Linker(_host.Engine);

        // Register capability-gated host functions
        _hostFunctionRegistry.RegisterHostFunctions(linker, grantedCapabilities, manifest.Name);

        var instance = linker.Instantiate(store, module);

        var plugin = new WasmPlugin(manifest, store, instance);
        _loadedPlugins[manifest.Name] = plugin;

        GantriMeters.PluginsLoaded.Add(1, new KeyValuePair<string, object?>(GantriSemanticConventions.PluginType, "wasm"));
        _logger.LogInformation("Loaded WASM plugin '{PluginName}' v{Version} with {ActionCount} actions, capabilities: {Capabilities}",
            manifest.Name, manifest.Version, plugin.ActionNames.Count, grantedCapabilities);

        return Task.FromResult<IPlugin>(plugin);
    }

    public async Task UnloadAsync(string pluginName, CancellationToken cancellationToken = default)
    {
        if (_loadedPlugins.Remove(pluginName, out var plugin))
        {
            await plugin.DisposeAsync();
            GantriMeters.PluginsLoaded.Add(-1, new KeyValuePair<string, object?>(GantriSemanticConventions.PluginType, "wasm"));
            _logger.LogInformation("Unloaded WASM plugin '{PluginName}'", pluginName);
        }
    }
}
