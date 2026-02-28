using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Gantri.Bridge;

/// <summary>
/// Implements <see cref="IWorkflowEngine"/> with AF routing.
/// Simple sequential agent workflows route to AF <see cref="AIAgent"/> sequential execution.
/// Complex workflows (parallel, approval, condition, plugin steps) delegate to the existing
/// <see cref="ILegacyWorkflowEngine"/> implementation as legacy fallback.
/// </summary>
public sealed class AfWorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowEngine _legacyEngine;
    private readonly GantriAgentFactory _agentFactory;
    private readonly IWorkflowDefinitionRegistry _workflowRegistry;
    private readonly IAgentDefinitionRegistry _agentRegistry;
    private readonly ILogger<AfWorkflowEngine> _logger;

    public AfWorkflowEngine(
        IWorkflowEngine legacyEngine,
        GantriAgentFactory agentFactory,
        IWorkflowDefinitionRegistry workflowRegistry,
        ILogger<AfWorkflowEngine> logger,
        IAgentDefinitionRegistry agentRegistry
    )
    {
        _legacyEngine = legacyEngine;
        _agentFactory = agentFactory;
        _workflowRegistry = workflowRegistry;
        _agentRegistry = agentRegistry;
        _logger = logger;
    }

    public async Task<WorkflowResult> ExecuteAsync(
        string workflowName,
        IReadOnlyDictionary<string, object?>? input = null,
        CancellationToken cancellationToken = default
    )
    {
        var definition =
            _workflowRegistry.TryGet(workflowName)
            ?? throw new InvalidOperationException(
                $"Workflow '{workflowName}' not found. Available: {string.Join(", ", _workflowRegistry.Names)}"
            );

        // Route simple sequential agent-only workflows through AF
        if (IsSimpleSequentialAgentWorkflow(definition))
        {
            _logger.LogInformation(
                "Routing workflow '{Workflow}' through AF sequential pipeline",
                workflowName
            );
            return await ExecuteViaAfAsync(definition, input, cancellationToken);
        }

        // Complex workflows delegate to legacy engine
        _logger.LogInformation("Routing workflow '{Workflow}' through legacy engine", workflowName);
        return await _legacyEngine.ExecuteAsync(workflowName, input, cancellationToken);
    }

    public Task<WorkflowResult> ResumeAsync(
        string executionId,
        CancellationToken cancellationToken = default
    )
    {
        // Resume always goes through legacy engine (AF sequential doesn't support pause/resume)
        return _legacyEngine.ResumeAsync(executionId, cancellationToken);
    }

    public IReadOnlyList<string> ListWorkflows() => _workflowRegistry.Names;

    public Task<IReadOnlyList<WorkflowRunInfo>> ListActiveRunsAsync(
        CancellationToken cancellationToken = default
    ) => _legacyEngine.ListActiveRunsAsync(cancellationToken);

    public Task<WorkflowRunStatus?> GetRunStatusAsync(
        string executionId,
        CancellationToken cancellationToken = default
    ) => _legacyEngine.GetRunStatusAsync(executionId, cancellationToken);

    private static bool IsSimpleSequentialAgentWorkflow(WorkflowDefinition definition)
    {
        // A workflow is "simple sequential agent" if ALL steps are agent type
        // with no parallel, approval, condition, or plugin steps
        return definition.Steps.Count > 0
            && definition.Steps.All(s =>
                s.Type.Equals("agent", StringComparison.OrdinalIgnoreCase) && s.Steps.Count == 0
            );
    }

    private async Task<WorkflowResult> ExecuteViaAfAsync(
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, object?>? input,
        CancellationToken cancellationToken
    )
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Get initial input from step definition or workflow input
            var currentInput =
                definition.Steps.FirstOrDefault()?.Input
                ?? input?.Values.FirstOrDefault()?.ToString()
                ?? string.Empty;

            var stepOutputs = new Dictionary<string, object?>();

            // Run each agent step sequentially, passing output to the next
            foreach (var step in definition.Steps)
            {
                if (step.Agent is null)
                    throw new InvalidOperationException(
                        $"Step '{step.Id}' has no agent specified."
                    );

                // Look up the full agent definition from config; fall back to a bare definition
                var agentDef =
                    _agentRegistry.TryGet(step.Agent)
                    ?? new AgentDefinition { Name = step.Agent, Model = "gpt-5-mini" };

                var afAgent = await _agentFactory.CreateAgentAsync(agentDef, cancellationToken);

                var session = await afAgent.CreateSessionAsync(
                    cancellationToken: cancellationToken
                );
                var response = await afAgent.RunAsync(
                    currentInput,
                    session,
                    cancellationToken: cancellationToken
                );
                var output = response.Text ?? string.Empty;

                stepOutputs[step.Id] = output;
                currentInput = output;

                _logger.LogInformation(
                    "AF workflow step '{Step}' completed ({Length} chars)",
                    step.Id,
                    output.Length
                );
            }

            sw.Stop();

            _logger.LogInformation(
                "AF workflow '{Workflow}' completed in {Duration}ms",
                definition.Name,
                sw.ElapsedMilliseconds
            );

            return WorkflowResult.Ok(stepOutputs, currentInput, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "AF workflow '{Workflow}' failed", definition.Name);
            return WorkflowResult.Fail($"Workflow failed: {ex.Message}", sw.Elapsed);
        }
    }
}
