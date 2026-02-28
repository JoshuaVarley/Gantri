namespace Gantri.Abstractions.Agents;

public interface IToolApprovalHandler
{
    Task<ToolApprovalResult> RequestApprovalAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);
}

public sealed class ToolApprovalResult
{
    public bool Approved { get; init; }
    public string? Reason { get; init; }

    public static ToolApprovalResult Approve() => new() { Approved = true };
    public static ToolApprovalResult Reject(string? reason = null) =>
        new() { Approved = false, Reason = reason };
}
