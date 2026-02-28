using System.Reflection;
using Gantri.Abstractions.Plugins;

namespace Gantri.Plugins.Native;

public sealed class NativePluginValidator
{
    private const string SdkPluginActionInterfaceName = "Gantri.Plugins.Sdk.ISdkPluginAction";

    public NativePluginValidationResult Validate(Assembly assembly, PluginManifest manifest)
    {
        var errors = new List<string>();

        if (manifest.Type != PluginType.Native)
            errors.Add($"Manifest type is '{manifest.Type}' but expected 'Native'.");

        // Use name-based matching to handle cross-AssemblyLoadContext type identity
        var actionTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface &&
                        t.GetInterfaces().Any(i => i.FullName == SdkPluginActionInterfaceName))
            .ToList();

        var declaredActions = manifest.Exports.Actions.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var foundActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in actionTypes)
        {
            var instance = Activator.CreateInstance(type);
            var actionNameProp = type.GetProperty("ActionName");
            if (actionNameProp?.GetValue(instance) is string actionName)
                foundActions.Add(actionName);
        }

        foreach (var declared in declaredActions)
        {
            if (!foundActions.Contains(declared))
                errors.Add($"Manifest declares action '{declared}' but no matching ISdkPluginAction found in assembly.");
        }

        return new NativePluginValidationResult(errors.Count == 0, errors);
    }
}

public sealed record NativePluginValidationResult(bool IsValid, IReadOnlyList<string> Errors);
