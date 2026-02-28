using Gantri.Abstractions.Agents;

namespace Gantri.Cli.Interactive;

/// <summary>
/// Console-based tool approval handler that prompts the user before each tool call.
/// Maintains a per-session set of always-approved tools.
/// </summary>
internal sealed class InteractiveToolApprovalHandler : IToolApprovalHandler
{
    private readonly ConsoleRenderer _renderer;
    private readonly HashSet<string> _alwaysApproved = new(StringComparer.OrdinalIgnoreCase);

    public InteractiveToolApprovalHandler(ConsoleRenderer renderer)
    {
        _renderer = renderer;
    }

    public Task<ToolApprovalResult> RequestApprovalAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (_alwaysApproved.Contains(toolName))
            return Task.FromResult(ToolApprovalResult.Approve());

        var choice = _renderer.RenderToolApproval(toolName, parameters);

        return choice switch
        {
            ToolApprovalChoice.Approve => Task.FromResult(ToolApprovalResult.Approve()),
            ToolApprovalChoice.AlwaysApprove => AlwaysApprove(toolName),
            _ => Task.FromResult(ToolApprovalResult.Reject("User rejected the tool call"))
        };
    }

    private Task<ToolApprovalResult> AlwaysApprove(string toolName)
    {
        _alwaysApproved.Add(toolName);
        return Task.FromResult(ToolApprovalResult.Approve());
    }
}
