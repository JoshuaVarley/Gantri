using Microsoft.Extensions.AI;

namespace Gantri.Abstractions.AI;

[Obsolete("Use Gantri.Bridge.GantriAgentFactory instead. Will be removed in a future version.")]
public interface IChatClientProvider
{
    IChatClient GetChatClient(string modelAlias, string? provider = null);
    IReadOnlyList<string> GetAvailableModels();
    IReadOnlyList<string> GetAvailableProviders();
}
