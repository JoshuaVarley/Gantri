using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Workflows;
using Gantri.Cli.Infrastructure;

namespace Gantri.Cli.Interactive;

/// <summary>
/// Shared state across the interactive console session.
/// </summary>
internal sealed class ConsoleContext
{
    public IAgentSession? ActiveSession { get; set; }
    public string? ActiveAgentName { get; set; }
    public int MessageCount { get; set; }

    public IAgentOrchestrator Orchestrator { get; }
    public IWorkflowEngine WorkflowEngine { get; }
    public WorkerMcpClient WorkerClient { get; }
    public ConsoleRenderer Renderer { get; }
    public bool ExitRequested { get; set; }

    public ConsoleContext(
        IAgentOrchestrator orchestrator,
        IWorkflowEngine workflowEngine,
        WorkerMcpClient workerClient,
        ConsoleRenderer renderer)
    {
        Orchestrator = orchestrator;
        WorkflowEngine = workflowEngine;
        WorkerClient = workerClient;
        Renderer = renderer;
    }

    public async Task EndSessionAsync()
    {
        if (ActiveSession is not null)
        {
            await ActiveSession.DisposeAsync();
            ActiveSession = null;
            ActiveAgentName = null;
            MessageCount = 0;
        }
    }
}
