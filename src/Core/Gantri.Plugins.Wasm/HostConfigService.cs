using System.Reflection;
using Gantri.Abstractions.Plugins;
using Gantri.Configuration;

namespace Gantri.Plugins.Wasm;

public sealed class HostConfigService : IHostConfigService
{
    private readonly GantriConfigRoot _config;

    public HostConfigService(GantriConfigRoot config)
    {
        _config = config;
    }

    public string? GetValue(string dotPath)
    {
        if (string.IsNullOrWhiteSpace(dotPath))
            return null;

        object? current = _config;
        foreach (var segment in dotPath.Split('.'))
        {
            if (current is null)
                return null;

            var property = current.GetType().GetProperty(
                segment,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null)
                return null;

            current = property.GetValue(current);
        }

        return current?.ToString();
    }
}
