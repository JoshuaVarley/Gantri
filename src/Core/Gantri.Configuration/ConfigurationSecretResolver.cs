using Gantri.Abstractions.Configuration;
using Microsoft.Extensions.Configuration;

namespace Gantri.Configuration;

public sealed class ConfigurationSecretResolver(IConfiguration configuration) : ISecretResolver
{
    public string? Resolve(string key)
        => configuration[key] ?? Environment.GetEnvironmentVariable(key);
}
