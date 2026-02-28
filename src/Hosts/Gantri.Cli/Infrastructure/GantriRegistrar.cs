using Spectre.Console.Cli;

namespace Gantri.Cli.Infrastructure;

/// <summary>
/// Bridges Spectre.Console.Cli command resolution with Microsoft.Extensions.DependencyInjection.
/// </summary>
internal sealed class GantriRegistrar : ITypeRegistrar
{
    private readonly IServiceProvider _provider;

    public GantriRegistrar(IServiceProvider provider)
    {
        _provider = provider;
    }

    public ITypeResolver Build() => new GantriResolver(_provider);

    public void Register(Type service, Type implementation)
    {
        // Spectre.Console registers its own types — we let the DI container handle ours
    }

    public void RegisterInstance(Type service, object implementation)
    {
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
    }
}

internal sealed class GantriResolver : ITypeResolver
{
    private readonly IServiceProvider _provider;

    public GantriResolver(IServiceProvider provider)
    {
        _provider = provider;
    }

    public object? Resolve(Type? type)
    {
        if (type is null) return null;
        return _provider.GetService(type) ?? Activator.CreateInstance(type);
    }
}
