using System.Reflection;
using System.Runtime.Loader;

namespace Gantri.Plugins.Native;

public sealed class NativePluginContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    // Assemblies that define the host-plugin contract must load from the host context
    // to preserve type identity across AssemblyLoadContext boundaries.
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Gantri.Abstractions",
        "Gantri.Plugins.Sdk"
    };

    public NativePluginContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && SharedAssemblies.Contains(assemblyName.Name))
            return null;

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is not null ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is not null ? LoadUnmanagedDllFromPath(libraryPath) : nint.Zero;
    }
}
