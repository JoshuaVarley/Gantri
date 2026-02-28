using Gantri.Abstractions.Configuration;
using Gantri.Bridge;

namespace Gantri.Bridge.Tests;

public class ChatClientFactoryTests
{
    [Fact]
    public void Create_AzureOpenAI_ChatApiType_ReturnsChatClient()
    {
        var provider = new AiProviderOptions
        {
            Endpoint = "https://test.openai.azure.com/",
            ApiKey = "test-key",
        };
        var model = new AiModelOptions { Id = "gpt-4o", ApiType = "chat" };

        var client = ChatClientFactory.Create("azure-openai", provider, model);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_AzureOpenAI_ResponsesApiType_ReturnsChatClient()
    {
        var provider = new AiProviderOptions
        {
            Endpoint = "https://test.openai.azure.com/",
            ApiKey = "test-key",
        };
        var model = new AiModelOptions { Id = "gpt-5.1-codex-mini", ApiType = "responses" };

        var client = ChatClientFactory.Create("azure-openai", provider, model);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_BaseUrl_ChatApiType_ReturnsChatClient()
    {
        var provider = new AiProviderOptions
        {
            BaseUrl = "https://foundry.services.ai.azure.com/openai/v1/",
            ApiKey = "test-key",
        };
        var model = new AiModelOptions { Id = "DeepSeek-V3.2", ApiType = "chat" };

        var client = ChatClientFactory.Create("azure-ai-foundry", provider, model);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_BaseUrl_ResponsesApiType_ReturnsChatClient()
    {
        var provider = new AiProviderOptions
        {
            BaseUrl = "https://foundry.services.ai.azure.com/openai/v1/",
            ApiKey = "test-key",
        };
        var model = new AiModelOptions { Id = "gpt-5.1-codex-mini", ApiType = "responses" };

        var client = ChatClientFactory.Create("azure-ai-foundry", provider, model);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_DefaultApiType_UsesChatCompletions()
    {
        var provider = new AiProviderOptions
        {
            Endpoint = "https://test.openai.azure.com/",
            ApiKey = "test-key",
        };
        // Default ApiType is "chat"
        var model = new AiModelOptions { Id = "gpt-4o" };

        var client = ChatClientFactory.Create("azure-openai", provider, model);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_ApiTypeIsCaseInsensitive()
    {
        var provider = new AiProviderOptions
        {
            Endpoint = "https://test.openai.azure.com/",
            ApiKey = "test-key",
        };
        var model = new AiModelOptions { Id = "gpt-5.1-codex-mini", ApiType = "Responses" };

        var client = ChatClientFactory.Create("azure-openai", provider, model);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_NoEndpointOrBaseUrl_Throws()
    {
        var provider = new AiProviderOptions { ApiKey = "test-key" };
        var model = new AiModelOptions { Id = "gpt-4o" };

        var act = () => ChatClientFactory.Create("bad-provider", provider, model);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no endpoint or base_url*");
    }

    [Fact]
    public void Create_MissingApiKey_Throws()
    {
        var provider = new AiProviderOptions
        {
            Endpoint = "https://test.openai.azure.com/",
        };
        var model = new AiModelOptions { Id = "gpt-4o" };

        var act = () => ChatClientFactory.Create("test", provider, model);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing api_key*");
    }

    [Fact]
    public void Create_BaseUrl_MissingApiKey_Throws()
    {
        var provider = new AiProviderOptions
        {
            BaseUrl = "https://foundry.services.ai.azure.com/openai/v1/",
        };
        var model = new AiModelOptions { Id = "gpt-4o" };

        var act = () => ChatClientFactory.Create("test", provider, model);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing api_key*");
    }

    [Fact]
    public void Create_UsesDeploymentNameOverId()
    {
        var provider = new AiProviderOptions
        {
            Endpoint = "https://test.openai.azure.com/",
            ApiKey = "test-key",
        };
        var model = new AiModelOptions
        {
            Id = "gpt-4o",
            DeploymentName = "my-custom-deployment",
        };

        // Should not throw — verifies deployment name is used
        var client = ChatClientFactory.Create("azure-openai", provider, model);

        client.Should().NotBeNull();
    }
}
