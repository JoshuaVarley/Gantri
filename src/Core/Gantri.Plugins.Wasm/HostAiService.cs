using Gantri.Abstractions.Plugins;
using Microsoft.Extensions.AI;

namespace Gantri.Plugins.Wasm;

public sealed class HostAiService : IHostAiService
{
    private readonly IChatClient _chatClient;

    public HostAiService(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> CompleteAsync(string prompt, string? model = null, CancellationToken cancellationToken = default)
    {
        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        return response.Text ?? string.Empty;
    }
}
