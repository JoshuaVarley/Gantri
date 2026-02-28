using System.Text.Json;
using Gantri.Abstractions.Plugins;
using Gantri.Telemetry;
using Microsoft.Extensions.Logging;

namespace Gantri.Plugins.Native;

public sealed class NativePluginLoader : IPluginLoader
{
    private const string SdkPluginActionInterfaceName = "Gantri.Plugins.Sdk.ISdkPluginAction";

    private readonly NativePluginValidator _validator;
    private readonly ILogger<NativePluginLoader> _logger;
    private readonly Dictionary<string, NativePlugin> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);

    public NativePluginLoader(NativePluginValidator validator, ILogger<NativePluginLoader> logger)
    {
        _validator = validator;
        _logger = logger;
    }

    public PluginType SupportedType => PluginType.Native;

    public bool CanLoad(PluginManifest manifest) => manifest.Type == PluginType.Native;

    public Task<IPlugin> LoadAsync(string pluginPath, PluginManifest manifest, CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.PluginsNative.StartActivity("gantri.plugins.native.load");
        activity?.SetTag(GantriSemanticConventions.PluginName, manifest.Name);

        var assemblyPath = ResolveAssemblyPath(pluginPath, manifest.Entry);
        if (assemblyPath is null)
            throw new FileNotFoundException($"Plugin assembly not found: {Path.Combine(pluginPath, manifest.Entry)}");

        var loadContext = new NativePluginContext(assemblyPath);
        var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

        var validationResult = _validator.Validate(assembly, manifest);
        if (!validationResult.IsValid)
        {
            loadContext.Unload();
            throw new InvalidOperationException(
                $"Plugin '{manifest.Name}' validation failed: {string.Join("; ", validationResult.Errors)}");
        }

        // Use name-based type matching to handle cross-AssemblyLoadContext type identity
        var actionTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface &&
                        t.GetInterfaces().Any(i => i.FullName == SdkPluginActionInterfaceName))
            .ToList();

        var actions = new Dictionary<string, ReflectionPluginActionAdapter>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in actionTypes)
        {
            var instance = Activator.CreateInstance(type)!;
            var adapter = new ReflectionPluginActionAdapter(instance, type);
            actions[adapter.ActionName] = adapter;
        }

        var plugin = new NativePlugin(manifest, actions, loadContext);
        _loadedPlugins[manifest.Name] = plugin;

        GantriMeters.PluginsLoaded.Add(1, new KeyValuePair<string, object?>(GantriSemanticConventions.PluginType, "native"));
        _logger.LogInformation("Loaded native plugin '{PluginName}' v{Version} with {ActionCount} actions",
            manifest.Name, manifest.Version, actions.Count);

        return Task.FromResult<IPlugin>(plugin);
    }

    public async Task UnloadAsync(string pluginName, CancellationToken cancellationToken = default)
    {
        if (_loadedPlugins.Remove(pluginName, out var plugin))
        {
            await plugin.DisposeAsync();
            GantriMeters.PluginsLoaded.Add(-1, new KeyValuePair<string, object?>(GantriSemanticConventions.PluginType, "native"));
            _logger.LogInformation("Unloaded native plugin '{PluginName}'", pluginName);
        }
    }

    public static PluginManifest LoadManifest(string pluginPath)
    {
        var manifestPath = Path.Combine(pluginPath, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Plugin manifest not found: {manifestPath}");

        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<PluginManifest>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize manifest at {manifestPath}");
    }

    /// <summary>
    /// Resolves the assembly path, checking the plugin root first, then build output directories.
    /// </summary>
    private static string? ResolveAssemblyPath(string pluginPath, string entry)
    {
        // Direct path (published/deployed plugins)
        var direct = Path.Combine(pluginPath, entry);
        if (File.Exists(direct))
            return direct;

        // Dev-time: search bin/{Debug,Release}/<tfm>/ directories
        var binDir = Path.Combine(pluginPath, "bin");
        if (!Directory.Exists(binDir))
            return null;

        foreach (var configDir in Directory.GetDirectories(binDir))
        {
            foreach (var tfmDir in Directory.GetDirectories(configDir))
            {
                var candidate = Path.Combine(tfmDir, entry);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
