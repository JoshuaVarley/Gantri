using System.Runtime.CompilerServices;
using Gantri.Abstractions.Agents;
using Gantri.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Gantri.Bridge;

/// <summary>
/// Implements <see cref="IAgentSession"/> by delegating to AF <see cref="AIAgent.RunAsync"/>.
/// Used by CLI and Worker hosts for string-in/string-out agent interaction.
/// For protocol-aware hosts (AG-UI, A2A), use <see cref="IAgentProvider"/> to get raw
/// <see cref="AIAgent"/> instances instead.
/// </summary>
public sealed class AfAgentSession : IAgentSession
{
    private readonly AIAgent _agent;
    private readonly AgentSession _session;
    private readonly ILogger _logger;

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];
    public string AgentName { get; }

    private AfAgentSession(AIAgent agent, AgentSession session, string agentName, ILogger logger)
    {
        _agent = agent;
        _session = session;
        _logger = logger;
        AgentName = agentName;
    }

    /// <summary>
    /// Creates a new <see cref="AfAgentSession"/> with an AF-managed session.
    /// </summary>
    public static async Task<AfAgentSession> CreateAsync(
        AIAgent agent, string agentName, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.Agents.StartActivity("gantri.agent.create_session");
        activity?.SetTag(GantriSemanticConventions.AgentName, agentName);

        GantriMeters.AgentSessionsTotal.Add(1,
            new KeyValuePair<string, object?>(GantriSemanticConventions.AgentName, agentName));

        var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
        var afSession = new AfAgentSession(agent, session, agentName, logger);

        activity?.SetTag(GantriSemanticConventions.AgentSessionId, afSession.SessionId);

        return afSession;
    }

    public async Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.Agents.StartActivity("gantri.agent.session");
        activity?.SetTag(GantriSemanticConventions.AgentName, AgentName);
        activity?.SetTag(GantriSemanticConventions.AgentSessionId, SessionId);

        GantriMeters.AgentSessionsActive.Add(1);

        try
        {
            _logger.LogInformation("Sending message to AF agent '{Agent}' (session: {SessionId})",
                AgentName, SessionId);

            var response = await _agent.RunAsync(message, _session, cancellationToken: cancellationToken);
            var text = response.Text;

            _logger.LogInformation("AF agent '{Agent}' responded ({Length} chars)",
                AgentName, text?.Length ?? 0);

            return text ?? string.Empty;
        }
        finally
        {
            GantriMeters.AgentSessionsActive.Add(-1);
        }
    }

    public async IAsyncEnumerable<string> SendMessageStreamingAsync(
        string message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.Agents.StartActivity("gantri.agent.session.streaming");
        activity?.SetTag(GantriSemanticConventions.AgentName, AgentName);
        activity?.SetTag(GantriSemanticConventions.AgentSessionId, SessionId);

        GantriMeters.AgentSessionsActive.Add(1);

        try
        {
            _logger.LogInformation("Sending streaming message to AF agent '{Agent}' (session: {SessionId})",
                AgentName, SessionId);

            await foreach (var item in _agent.RunStreamingAsync(message, _session, cancellationToken: cancellationToken))
            {
                if (item.Text is not null)
                    yield return item.Text;
            }

            _logger.LogInformation("AF agent '{Agent}' streaming response completed", AgentName);
        }
        finally
        {
            GantriMeters.AgentSessionsActive.Add(-1);
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
