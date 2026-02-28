using Gantri.Abstractions.Configuration;
using Gantri.AI;

namespace Gantri.AI.Tests;

public class ModelProviderRegistryTests
{
    private readonly ModelProviderRegistry _sut = new();

    private static AiProviderOptions CreateProvider(params (string Alias, string Id)[] models)
    {
        var provider = new AiProviderOptions
        {
            ApiKey = "test-key",
            Endpoint = "https://test.openai.azure.com"
        };
        foreach (var (alias, id) in models)
        {
            provider.Models[alias] = new AiModelOptions { Id = id };
        }
        return provider;
    }

    [Fact]
    public void RegisterProvider_AddsProviderAndModels()
    {
        var options = CreateProvider(("gpt-4o", "gpt-4o-2024"), ("gpt-mini", "gpt-4o-mini"));

        _sut.RegisterProvider("azure", options);

        _sut.GetProvider("azure").Should().BeSameAs(options);
        _sut.GetAvailableModels().Should().Contain("gpt-4o").And.Contain("gpt-mini");
        _sut.GetAvailableProviders().Should().Contain("azure");
    }

    [Fact]
    public void ResolveModel_WithProviderHint_ReturnsCorrectModel()
    {
        var options = CreateProvider(("gpt-4o", "gpt-4o-2024"));
        _sut.RegisterProvider("azure", options);

        var (provider, model) = _sut.ResolveModel("gpt-4o", providerHint: "azure");

        provider.Should().Be("azure");
        model.Id.Should().Be("gpt-4o-2024");
    }

    [Fact]
    public void ResolveModel_WithoutProviderHint_ResolvesFromAnyProvider()
    {
        var options = CreateProvider(("gpt-4o", "gpt-4o-2024"));
        _sut.RegisterProvider("azure", options);

        var (provider, model) = _sut.ResolveModel("gpt-4o");

        provider.Should().Be("azure");
        model.Id.Should().Be("gpt-4o-2024");
    }

    [Fact]
    public void ResolveModel_UnknownAlias_ThrowsInvalidOperation()
    {
        var act = () => _sut.ResolveModel("nonexistent");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*nonexistent*not found*");
    }

    [Fact]
    public void ResolveModel_WrongProviderHint_ThrowsInvalidOperation()
    {
        var options = CreateProvider(("gpt-4o", "gpt-4o-2024"));
        _sut.RegisterProvider("azure", options);

        var act = () => _sut.ResolveModel("gpt-4o", providerHint: "openai");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void GetAvailableModels_ReturnsAllAliases()
    {
        _sut.RegisterProvider("azure", CreateProvider(("gpt-4o", "gpt-4o-2024")));
        _sut.RegisterProvider("openai", CreateProvider(("claude", "claude-3")));

        var models = _sut.GetAvailableModels();

        models.Should().HaveCount(2);
        models.Should().Contain("gpt-4o");
        models.Should().Contain("claude");
    }

    [Fact]
    public void GetAvailableProviders_ReturnsAllProviders()
    {
        _sut.RegisterProvider("azure", CreateProvider(("m1", "id1")));
        _sut.RegisterProvider("openai", CreateProvider(("m2", "id2")));

        var providers = _sut.GetAvailableProviders();

        providers.Should().HaveCount(2);
        providers.Should().Contain("azure");
        providers.Should().Contain("openai");
    }

    [Fact]
    public void GetProvider_ReturnsNull_ForUnknown()
    {
        var provider = _sut.GetProvider("nonexistent");

        provider.Should().BeNull();
    }
}
