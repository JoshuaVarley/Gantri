namespace Gantri.Abstractions.Plugins;

/// <summary>
/// Provides AI completion capabilities to WASM plugins.
/// </summary>
public interface IHostAiService
{
    Task<string> CompleteAsync(string prompt, string? model = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides configuration read access to WASM plugins.
/// </summary>
public interface IHostConfigService
{
    string? GetValue(string dotPath);
}

/// <summary>
/// Provides MCP tool invocation to WASM plugins.
/// </summary>
public interface IHostMcpService
{
    Task<string> InvokeToolAsync(string serverName, string toolName, string argumentsJson, CancellationToken cancellationToken = default);
}
