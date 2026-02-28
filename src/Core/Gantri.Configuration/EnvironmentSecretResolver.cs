using Gantri.Abstractions.Configuration;

namespace Gantri.Configuration;

public sealed class EnvironmentSecretResolver : ISecretResolver
{
    public static readonly EnvironmentSecretResolver Instance = new();

    public string? Resolve(string key) => Environment.GetEnvironmentVariable(key);
}
