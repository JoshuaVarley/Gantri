using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Gantri.Bridge;

/// <summary>
/// Implements <see cref="IAgentOrchestrator"/> using <see cref="GantriAgentFactory"/>.
/// Used by CLI and Worker hosts for string-based agent interaction and group chat orchestration.
/// For protocol-aware hosts (AG-UI, A2A), use <see cref="IAgentProvider"/> to get raw
/// <see cref="AIAgent"/> instances instead.
/// </summary>
public sealed class AfAgentOrchestrator : IAgentOrchestrator
{
    private readonly GantriAgentFactory _agentFactory;
    private readonly IHookPipeline _hookPipeline;
    private readonly IAgentDefinitionRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AfAgentOrchestrator> _logger;

    public AfAgentOrchestrator(
        GantriAgentFactory agentFactory,
        IHookPipeline hookPipeline,
        IAgentDefinitionRegistry registry,
        ILoggerFactory loggerFactory)
    {
        _agentFactory = agentFactory;
        _hookPipeline = hookPipeline;
        _registry = registry;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<AfAgentOrchestrator>();
    }

    public async Task<IAgentSession> CreateSessionAsync(string agentName, CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.Agents.StartActivity("gantri.agent.create_session");
        activity?.SetTag(GantriSemanticConventions.AgentName, agentName);

        var definition = _registry.TryGet(agentName)
            ?? throw new InvalidOperationException(
                $"Agent '{agentName}' not found. Available agents: {string.Join(", ", _registry.Names)}");

        // Build AF AIAgent via factory
        var afAgent = await _agentFactory.CreateAgentAsync(definition, cancellationToken);

        var session = await AfAgentSession.CreateAsync(
            afAgent,
            agentName,
            _loggerFactory.CreateLogger<AfAgentSession>(),
            cancellationToken);

        // Fire session-start hook
        var hookEvent = new HookEvent("agent", agentName, "session-start", HookTiming.Before);
        await _hookPipeline.ExecuteAsync(hookEvent, _ => ValueTask.CompletedTask,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Created AF agent session for '{Agent}'", agentName);

        return session;
    }

    public Task<IReadOnlyList<string>> ListAgentsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(_registry.Names);
    }

    public async Task<string> RunGroupChatAsync(
        IReadOnlyList<string> participants,
        string input,
        int maxIterations = 5,
        CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.Agents.StartActivity("gantri.bridge.group_chat");

        // Fire group-chat start hook
        var startEvent = new HookEvent("orchestration", "group-chat", "start", HookTiming.Before);
        var startCtx = new HookContext(startEvent, cancellationToken);
        startCtx.Set("participants", participants);
        startCtx.Set("input", input);
        await _hookPipeline.ExecuteAsync(startEvent, _ => ValueTask.CompletedTask,
            startCtx, cancellationToken);

        _logger.LogInformation("Starting group chat with {Count} participants: {Participants}",
            participants.Count, string.Join(", ", participants));

        // Build AF agents for all participants
        var agents = new List<(string Name, AIAgent Agent)>();
        foreach (var participantName in participants)
        {
            var definition = _registry.TryGet(participantName)
                ?? throw new InvalidOperationException(
                    $"Agent '{participantName}' not found. Available: {string.Join(", ", _registry.Names)}");

            var afAgent = await _agentFactory.CreateAgentAsync(definition, cancellationToken);
            agents.Add((participantName, afAgent));
        }

        // Run sequential pipeline: each agent's output feeds the next agent's input
        var currentMessage = input;
        foreach (var (name, agent) in agents)
        {
            _logger.LogInformation("Running agent '{Agent}' in group chat", name);

            var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
            var response = await agent.RunAsync(currentMessage, session, cancellationToken: cancellationToken);
            var responseText = response.Text ?? string.Empty;

            _logger.LogInformation("Agent '{Agent}' responded ({Length} chars)", name, responseText.Length);

            currentMessage = responseText;
            GantriMeters.AiCompletionsTotal.Add(1);
        }

        // Fire group-chat end hook
        var endEvent = new HookEvent("orchestration", "group-chat", "end", HookTiming.After);
        var endCtx = new HookContext(endEvent, cancellationToken);
        endCtx.Set("output", currentMessage);
        await _hookPipeline.ExecuteAsync(endEvent, _ => ValueTask.CompletedTask,
            endCtx, cancellationToken);

        _logger.LogInformation("Group chat completed ({Length} chars output)", currentMessage.Length);
        return currentMessage;
    }
}
