using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Configuration;

namespace Gantri.Workflows.Steps;

/// <summary>
/// Executes a workflow step by running an agent with resolved input.
/// </summary>
public sealed class AgentStepHandler : IStepHandler
{
    private readonly IAgentOrchestrator _orchestrator;

    public string StepType => "agent";

    public AgentStepHandler(IAgentOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<StepResult> ExecuteAsync(WorkflowStepDefinition step, WorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(step.Agent))
            return StepResult.Fail($"Step '{step.Id}' is type 'agent' but has no agent specified.");

        var input = context.ResolveTemplate(step.Input);

        try
        {
            await using var session = await _orchestrator.CreateSessionAsync(step.Agent, cancellationToken);
            var response = await session.SendMessageAsync(input, cancellationToken);
            return StepResult.Ok(response);
        }
        catch (Exception ex)
        {
            return StepResult.Fail($"Agent '{step.Agent}' failed: {ex.Message}");
        }
    }
}
