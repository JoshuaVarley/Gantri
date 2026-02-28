using System.Text.RegularExpressions;
using Gantri.Abstractions.Configuration;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Gantri.Configuration;

public sealed partial class YamlConfigurationLoader
{
    private readonly ILogger<YamlConfigurationLoader> _logger;
    private readonly ISecretResolver _secretResolver;
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public YamlConfigurationLoader(ILogger<YamlConfigurationLoader> logger)
        : this(logger, EnvironmentSecretResolver.Instance)
    {
    }

    public YamlConfigurationLoader(ILogger<YamlConfigurationLoader> logger, ISecretResolver secretResolver)
    {
        _logger = logger;
        _secretResolver = secretResolver;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    public T Load<T>(string filePath) where T : new()
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Configuration file not found: {filePath}");

        var yaml = File.ReadAllText(filePath);
        yaml = SubstituteEnvironmentVariables(yaml);

        try
        {
            return _deserializer.Deserialize<T>(yaml) ?? new T();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse YAML configuration at {filePath}: {ex.Message}", ex);
        }
    }

    public T LoadTypedWithImports<T>(string rootPath) where T : new()
    {
        var merged = LoadWithImports(rootPath);
        var yaml = _serializer.Serialize(merged);

        try
        {
            return _deserializer.Deserialize<T>(yaml) ?? new T();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize merged configuration from {rootPath}: {ex.Message}", ex);
        }
    }

    public Dictionary<string, object?> LoadRaw(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Configuration file not found: {filePath}");

        var yaml = File.ReadAllText(filePath);
        yaml = SubstituteEnvironmentVariables(yaml);

        return _deserializer.Deserialize<Dictionary<string, object?>>(yaml)
            ?? new Dictionary<string, object?>();
    }

    public Dictionary<string, object?> LoadWithImports(string rootPath)
    {
        var root = LoadRaw(rootPath);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(rootPath)) ?? ".";

        if (root.TryGetValue("framework", out var frameworkObj) &&
            frameworkObj is Dictionary<object, object> framework &&
            framework.TryGetValue("imports", out var importsObj) &&
            importsObj is List<object> imports)
        {
            foreach (var import in imports.OfType<string>())
            {
                var resolvedPaths = ResolveImportPaths(baseDir, import);

                foreach (var importPath in resolvedPaths)
                {
                    var imported = LoadRaw(importPath);
                    DeepMerge(root, imported);
                    _logger.LogDebug("Imported config from {Path}", importPath);
                }
            }
        }

        return root;
    }

    private static IEnumerable<string> ResolveImportPaths(string baseDir, string import)
    {
        if (import.Contains('*'))
        {
            var pattern = Path.GetFileName(import);
            var dir = Path.Combine(baseDir, Path.GetDirectoryName(import) ?? ".");

            if (!Directory.Exists(dir))
                return [];

            return Directory.GetFiles(dir, pattern).OrderBy(f => f, StringComparer.Ordinal);
        }

        var fullPath = Path.Combine(baseDir, import);
        return File.Exists(fullPath) ? [fullPath] : [];
    }

    internal static void DeepMerge(Dictionary<string, object?> target, Dictionary<string, object?> source)
    {
        foreach (var (key, sourceValue) in source)
        {
            if (sourceValue is Dictionary<object, object> sourceDict &&
                target.TryGetValue(key, out var targetValue) &&
                targetValue is Dictionary<object, object> targetDict)
            {
                DeepMergeUntyped(targetDict, sourceDict);
            }
            else
            {
                target[key] = sourceValue;
            }
        }
    }

    private static void DeepMergeUntyped(Dictionary<object, object> target, Dictionary<object, object> source)
    {
        foreach (var (key, sourceValue) in source)
        {
            if (sourceValue is Dictionary<object, object> sourceDict &&
                target.TryGetValue(key, out var targetValue) &&
                targetValue is Dictionary<object, object> targetDict)
            {
                DeepMergeUntyped(targetDict, sourceDict);
            }
            else
            {
                target[key] = sourceValue;
            }
        }
    }

    internal string SubstituteEnvironmentVariables(string yaml)
    {
        return EnvVarPattern().Replace(yaml, match =>
        {
            var varName = match.Groups[1].Value;
            return _secretResolver.Resolve(varName) ?? match.Value;
        });
    }

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex EnvVarPattern();
}
