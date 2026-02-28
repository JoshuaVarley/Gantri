using System.Text.Json;
using Gantri.Abstractions.Mcp;
using Gantri.Abstractions.Plugins;

namespace Gantri.Plugins.Wasm;

public sealed class HostMcpService : IHostMcpService
{
    private readonly IMcpToolProvider _mcpToolProvider;

    public HostMcpService(IMcpToolProvider mcpToolProvider)
    {
        _mcpToolProvider = mcpToolProvider;
    }

    public async Task<string> InvokeToolAsync(string serverName, string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?>? args = null;
        if (!string.IsNullOrWhiteSpace(argumentsJson))
            args = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson);

        var result = await _mcpToolProvider.InvokeToolAsync(serverName, toolName, args, cancellationToken);
        return result.Success ? result.Content?.ToString() ?? "" : $"Error: {result.Error}";
    }
}
