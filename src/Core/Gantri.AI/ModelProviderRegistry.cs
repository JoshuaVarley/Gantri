using Gantri.Abstractions.Configuration;

namespace Gantri.AI;

public sealed class ModelProviderRegistry
{
    private readonly Dictionary<string, AiProviderOptions> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Provider, AiModelOptions Model)> _modelAliases = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterProvider(string name, AiProviderOptions options)
    {
        _providers[name] = options;
        foreach (var (alias, model) in options.Models)
        {
            _modelAliases[alias] = (name, model);
        }
    }

    public (string Provider, AiModelOptions Model) ResolveModel(string alias, string? providerHint = null)
    {
        if (providerHint is not null)
        {
            if (_providers.TryGetValue(providerHint, out var provider) &&
                provider.Models.TryGetValue(alias, out var model))
            {
                return (providerHint, model);
            }

            throw new InvalidOperationException($"Model '{alias}' not found in provider '{providerHint}'.");
        }

        if (_modelAliases.TryGetValue(alias, out var resolved))
            return resolved;

        throw new InvalidOperationException(
            $"Model alias '{alias}' not found. Available aliases: {string.Join(", ", _modelAliases.Keys)}");
    }

    public AiProviderOptions? GetProvider(string name)
    {
        _providers.TryGetValue(name, out var provider);
        return provider;
    }

    public IReadOnlyList<string> GetAvailableModels() => _modelAliases.Keys.ToList();
    public IReadOnlyList<string> GetAvailableProviders() => _providers.Keys.ToList();
}
