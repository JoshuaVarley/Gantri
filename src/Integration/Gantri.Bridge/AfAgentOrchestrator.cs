using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Telemetry;
using Microsoft.Extensions.Logging;

namespace Gantri.Bridge;

/// <summary>
/// Implements <see cref="IAgentOrchestrator"/> using <see cref="GantriAgentFactory"/>.
/// Drop-in replacement for the old AgentOrchestrator.
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
        var orchestrator = new GroupChatOrchestrator(
            _agentFactory, _hookPipeline, _registry,
            _loggerFactory.CreateLogger<GroupChatOrchestrator>());

        return await orchestrator.RunAsync(participants, input, maxIterations, cancellationToken);
    }
}
