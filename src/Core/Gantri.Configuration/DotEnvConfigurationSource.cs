using Microsoft.Extensions.Configuration;

namespace Gantri.Configuration;

public sealed class DotEnvConfigurationSource : IConfigurationSource
{
    public string Path { get; set; } = ".env";
    public bool Optional { get; set; } = true;

    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new DotEnvConfigurationProvider(Path, Optional);
}

public sealed class DotEnvConfigurationProvider : ConfigurationProvider
{
    private readonly string _path;
    private readonly bool _optional;

    public DotEnvConfigurationProvider(string path, bool optional)
    {
        _path = path;
        _optional = optional;
    }

    public override void Load()
    {
        // Resolve relative paths against the executable's directory so the .env
        // file is found when placed next to the published exe, regardless of the
        // process's current working directory.
        var resolvedPath = Path.IsPathRooted(_path)
            ? _path
            : Path.Combine(AppContext.BaseDirectory, _path);

        if (!File.Exists(resolvedPath))
        {
            if (!_optional)
                throw new FileNotFoundException($".env file not found: {resolvedPath}");
            return;
        }

        foreach (var line in File.ReadLines(resolvedPath))
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            if (!TryParseKeyValue(trimmed, out var key, out var value))
                continue;

            Data[key] = value;

            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static bool TryParseKeyValue(string line, out string key, out string value)
    {
        const string exportPrefix = "export ";
        var content = line.StartsWith(exportPrefix, StringComparison.OrdinalIgnoreCase)
            ? line[exportPrefix.Length..].TrimStart()
            : line;

        var separatorIndex = content.IndexOf('=');
        if (separatorIndex <= 0)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = content[..separatorIndex].Trim();
        value = content[(separatorIndex + 1)..].Trim();

        if (
            value.Length >= 2
            && (
                (value.StartsWith('"') && value.EndsWith('"'))
                || (value.StartsWith('\'') && value.EndsWith('\''))
            )
        )
        {
            value = value[1..^1];
        }

        return key.Length > 0;
    }
}

public static class DotEnvConfigurationExtensions
{
    public static IConfigurationBuilder AddDotEnvFile(
        this IConfigurationBuilder builder,
        string path = ".env",
        bool optional = true
    )
    {
        builder.Add(new DotEnvConfigurationSource { Path = path, Optional = optional });
        return builder;
    }
}
