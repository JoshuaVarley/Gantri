namespace Gantri.Abstractions.Agents;

public interface IAgentOrchestrator
{
    Task<IAgentSession> CreateSessionAsync(string agentName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAgentsAsync(CancellationToken cancellationToken = default);
    Task<string> RunGroupChatAsync(IReadOnlyList<string> participants, string input, int maxIterations = 5, CancellationToken cancellationToken = default);
}
