#pragma warning disable OPENAI001

using System.ClientModel;
using Azure;
using Azure.AI.OpenAI;
using Gantri.Abstractions.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Gantri.Bridge;

/// <summary>
/// Creates <see cref="IChatClient"/> instances from provider configuration,
/// supporting both Chat Completions and Responses API endpoints.
/// </summary>
public static class ChatClientFactory
{
    // LLM models can take significant time before producing the first token
    // (reasoning, tool planning, etc.). The Azure SDK default of 100s is too short.
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(5);

    public static IChatClient Create(
        string providerName,
        AiProviderOptions providerOpts,
        AiModelOptions model)
    {
        var useResponses = string.Equals(model.ApiType, "responses", StringComparison.OrdinalIgnoreCase);

        // OpenAI-compatible endpoint (Azure AI Foundry model inference, etc.)
        if (providerOpts.BaseUrl is not null)
        {
            var credential = new ApiKeyCredential(
                providerOpts.ApiKey
                    ?? throw new InvalidOperationException(
                        $"Provider '{providerName}' is missing api_key."
                    )
            );
            var modelId = model.DeploymentName ?? model.Id;

            if (useResponses)
            {
                var client = new OpenAIClient(
                    credential: credential,
                    options: new OpenAIClientOptions
                    {
                        Endpoint = new Uri(providerOpts.BaseUrl),
                        NetworkTimeout = NetworkTimeout
                    }
                );
                return client.GetResponsesClient(modelId).AsIChatClient();
            }

            var chatClient = new ChatClient(
                credential: credential,
                model: modelId,
                options: new OpenAIClientOptions
                {
                    Endpoint = new Uri(providerOpts.BaseUrl),
                    NetworkTimeout = NetworkTimeout
                }
            );
            return chatClient.AsIChatClient();
        }

        // Azure OpenAI: requires Endpoint
        if (providerOpts.Endpoint is not null)
        {
            var endpoint = new Uri(providerOpts.Endpoint);
            var credential = new AzureKeyCredential(
                providerOpts.ApiKey
                    ?? throw new InvalidOperationException(
                        $"Provider '{providerName}' is missing api_key."
                    )
            );
            var azureClient = new AzureOpenAIClient(
                endpoint,
                credential,
                new AzureOpenAIClientOptions { NetworkTimeout = NetworkTimeout });
            var deploymentName = model.DeploymentName ?? model.Id;

            if (useResponses)
                return azureClient.GetResponsesClient(deploymentName).AsIChatClient();

            return azureClient.GetChatClient(deploymentName).AsIChatClient();
        }

        throw new InvalidOperationException(
            $"Provider '{providerName}' has no endpoint or base_url configured."
        );
    }
}
