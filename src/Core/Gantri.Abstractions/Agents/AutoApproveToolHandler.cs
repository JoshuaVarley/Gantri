namespace Gantri.Abstractions.Agents;

/// <summary>
/// Default implementation that auto-approves all tool calls.
/// Used by Worker, tests, and non-interactive CLI mode.
/// </summary>
public sealed class AutoApproveToolHandler : IToolApprovalHandler
{
    public Task<ToolApprovalResult> RequestApprovalAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ToolApprovalResult.Approve());
}
