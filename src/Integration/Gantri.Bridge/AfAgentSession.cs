using System.Diagnostics;
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
    private readonly ActivityContext _parentContext;
    private int _messageIndex;

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];
    public string AgentName { get; }
    public string ConversationId { get; }

    private AfAgentSession(AIAgent agent, AgentSession session, string agentName,
        ILogger logger, ActivityContext parentContext)
    {
        _agent = agent;
        _session = session;
        _logger = logger;
        _parentContext = parentContext;
        AgentName = agentName;
        ConversationId = Guid.NewGuid().ToString("N")[..12];
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

        // Capture the parent context so SendMessageAsync can re-parent under it.
        // In CLI: this is the gantri.agent.conversation span.
        // In API: this is the HTTP request span (or none).
        var parentContext = Activity.Current?.Context ?? default;

        var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
        var afSession = new AfAgentSession(agent, session, agentName, logger, parentContext);

        activity?.SetTag(GantriSemanticConventions.AgentSessionId, afSession.SessionId);
        activity?.SetTag(GantriSemanticConventions.AgentConversationId, afSession.ConversationId);

        return afSession;
    }

    public async Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        var msgIndex = Interlocked.Increment(ref _messageIndex);

        // Use explicit parent context instead of relying on ambient Activity.Current.
        // This ensures the span parents correctly even in API scenarios where
        // Activity.Current may be a different HTTP request span.
        using var activity = GantriActivitySources.Agents.StartActivity(
            "gantri.agent.session",
            ActivityKind.Internal,
            _parentContext);

        // Gantri-specific attributes
        activity?.SetTag(GantriSemanticConventions.AgentName, AgentName);
        activity?.SetTag(GantriSemanticConventions.AgentSessionId, SessionId);
        activity?.SetTag(GantriSemanticConventions.AgentConversationId, ConversationId);
        activity?.SetTag(GantriSemanticConventions.AgentMessageIndex, msgIndex);

        // GenAI standard attributes (for cross-tool compatibility)
        activity?.SetTag(GantriSemanticConventions.GenAiConversationId, ConversationId);
        activity?.SetTag(GantriSemanticConventions.GenAiAgentName, AgentName);

        // Propagate as baggage so descendant spans (M.E.AI, M.Agents.AI) inherit these
        activity?.SetBaggage(GantriSemanticConventions.AgentConversationId, ConversationId);
        activity?.SetBaggage(GantriSemanticConventions.AgentSessionId, SessionId);

        GantriMeters.AgentSessionsActive.Add(1);
        GantriMeters.AgentMessagesTotal.Add(1,
            new KeyValuePair<string, object?>(GantriSemanticConventions.AgentName, AgentName));

        try
        {
            _logger.LogInformation("Sending message to AF agent '{Agent}' (session: {SessionId}, message: {Index})",
                AgentName, SessionId, msgIndex);

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
        var msgIndex = Interlocked.Increment(ref _messageIndex);

        using var activity = GantriActivitySources.Agents.StartActivity(
            "gantri.agent.session.streaming",
            ActivityKind.Internal,
            _parentContext);

        activity?.SetTag(GantriSemanticConventions.AgentName, AgentName);
        activity?.SetTag(GantriSemanticConventions.AgentSessionId, SessionId);
        activity?.SetTag(GantriSemanticConventions.AgentConversationId, ConversationId);
        activity?.SetTag(GantriSemanticConventions.AgentMessageIndex, msgIndex);
        activity?.SetTag(GantriSemanticConventions.GenAiConversationId, ConversationId);
        activity?.SetTag(GantriSemanticConventions.GenAiAgentName, AgentName);

        activity?.SetBaggage(GantriSemanticConventions.AgentConversationId, ConversationId);
        activity?.SetBaggage(GantriSemanticConventions.AgentSessionId, SessionId);

        GantriMeters.AgentSessionsActive.Add(1);
        GantriMeters.AgentMessagesTotal.Add(1,
            new KeyValuePair<string, object?>(GantriSemanticConventions.AgentName, AgentName));

        try
        {
            _logger.LogInformation("Sending streaming message to AF agent '{Agent}' (session: {SessionId}, message: {Index})",
                AgentName, SessionId, msgIndex);

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
