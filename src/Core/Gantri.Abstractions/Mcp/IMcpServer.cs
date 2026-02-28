namespace Gantri.Abstractions.Mcp;

public interface IMcpServer
{
    string Name { get; }
    string Transport { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<McpToolInfo>> DiscoverToolsAsync(CancellationToken cancellationToken = default);
    Task<McpToolResult> InvokeToolAsync(string toolName, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default);
}
