namespace Gantri.Abstractions.Agents;

public interface IAgentSession : IAsyncDisposable
{
    string SessionId { get; }
    string AgentName { get; }
    string ConversationId { get; }
    Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> SendMessageStreamingAsync(string message, CancellationToken cancellationToken = default);
}
