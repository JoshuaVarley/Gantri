namespace Gantri.Abstractions.Mcp;

public interface IMcpToolProvider
{
    Task<IReadOnlyList<McpToolInfo>> GetToolsAsync(string? serverName = null, CancellationToken cancellationToken = default);
    Task<McpToolResult> InvokeToolAsync(string serverName, string toolName, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default);
}

public sealed class McpToolInfo
{
    public string ServerName { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? InputSchema { get; init; }
}

public sealed class McpToolResult
{
    public bool Success { get; init; }
    public object? Content { get; init; }
    public string? Error { get; init; }

    public static McpToolResult Ok(object? content = null) => new() { Success = true, Content = content };
    public static McpToolResult Fail(string error) => new() { Success = false, Error = error };
}
