namespace Gantri.Abstractions.Configuration;

public interface ISecretResolver
{
    string? Resolve(string key);
}
