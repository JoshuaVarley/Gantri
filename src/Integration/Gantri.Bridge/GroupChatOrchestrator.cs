using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Hooks;
using Gantri.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Gantri.Bridge;

/// <summary>
/// Orchestrates multi-agent group chat using AF <see cref="AIAgent"/> instances.
/// Runs participants sequentially, passing each agent's output as the next agent's input.
/// Fires <c>orchestration:group-chat:start/end</c> hook events.
/// </summary>
/// <remarks>
/// AF's GroupChatBuilder is currently Python-only. C# uses sequential orchestration.
/// If AF ships C# group chat builders later, only this file needs to change.
/// </remarks>
public sealed class GroupChatOrchestrator
{
    private readonly GantriAgentFactory _agentFactory;
    private readonly IHookPipeline _hookPipeline;
    private readonly IAgentDefinitionRegistry _registry;
    private readonly ILogger<GroupChatOrchestrator> _logger;

    public GroupChatOrchestrator(
        GantriAgentFactory agentFactory,
        IHookPipeline hookPipeline,
        IAgentDefinitionRegistry registry,
        ILogger<GroupChatOrchestrator> logger)
    {
        _agentFactory = agentFactory;
        _hookPipeline = hookPipeline;
        _registry = registry;
        _logger = logger;
    }

    public async Task<string> RunAsync(
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

        // Run sequential group chat: each agent's output feeds the next agent's input
        var currentMessage = input;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            _logger.LogInformation("Group chat iteration {Iteration}/{Max}", iteration + 1, maxIterations);

            foreach (var (name, agent) in agents)
            {
                _logger.LogInformation("Running agent '{Agent}' in group chat", name);

                var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
                var response = await agent.RunAsync(currentMessage, session, cancellationToken: cancellationToken);
                var responseText = response.Text ?? string.Empty;

                _logger.LogInformation("Agent '{Agent}' responded ({Length} chars)", name, responseText.Length);

                // The output of this agent becomes the input for the next
                currentMessage = responseText;

                GantriMeters.AiCompletionsTotal.Add(1);
            }

            // For single-pass sequential group chat, one iteration is typically sufficient
            // Additional iterations would re-run the pipeline for refinement
            if (iteration == 0)
                break;
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
