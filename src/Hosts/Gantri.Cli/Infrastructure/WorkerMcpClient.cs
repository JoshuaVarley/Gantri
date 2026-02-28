using System.Text.Json;
using Gantri.Abstractions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Gantri.Cli.Infrastructure;

/// <summary>
/// MCP client that connects to the Worker's MCP server for remote management.
/// </summary>
public sealed class WorkerMcpClient : IAsyncDisposable
{
    private readonly WorkerOptions _workerOptions;
    private IMcpClient? _client;

    public WorkerMcpClient(WorkerOptions workerOptions)
    {
        _workerOptions = workerOptions;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
            return;

        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments =
                [
                    "run",
                    "--no-launch-profile",
                    "--project",
                    "src/Hosts/Gantri.Worker",
                    "--",
                    "--mcp",
                ],
            }
        );

        _client = await McpClientFactory.CreateAsync(
            transport,
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "gantri-cli", Version = "0.1.0" },
            },
            cancellationToken: cancellationToken
        );
    }

    public async Task<string> CallToolAsync(
        string toolName,
        Dictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_client is null)
            throw new InvalidOperationException(
                "WorkerMcpClient is not connected. Call ConnectAsync first."
            );

        var result = await _client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: cancellationToken
        );
        return result.Content.FirstOrDefault()?.Text ?? "{}";
    }

    public async Task<T?> CallToolAsync<T>(
        string toolName,
        Dictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default
    )
    {
        var json = await CallToolAsync(toolName, arguments, cancellationToken);
        return JsonSerializer.Deserialize<T>(json);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }
}
