using System.Collections.Concurrent;
using Gantri.Abstractions.Mcp;
using Gantri.Telemetry;
using Microsoft.Extensions.Logging;

namespace Gantri.Mcp;

public sealed class McpClientManager : IMcpToolProvider
{
    private readonly ConcurrentDictionary<string, IMcpServer> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<McpClientManager> _logger;

    public McpClientManager(ILogger<McpClientManager> logger)
    {
        _logger = logger;
    }

    public void RegisterServer(IMcpServer server)
    {
        _servers[server.Name] = server;
        _logger.LogInformation("Registered MCP server '{Name}' ({Transport})", server.Name, server.Transport);
    }

    public async Task ConnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var server in _servers.Values)
        {
            if (!server.IsConnected)
            {
                await server.ConnectAsync(cancellationToken);
                _logger.LogInformation("Connected to MCP server '{Name}'", server.Name);
            }
        }
    }

    public async Task DisconnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var server in _servers.Values)
        {
            if (server.IsConnected)
            {
                await server.DisconnectAsync(cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<McpToolInfo>> GetToolsAsync(string? serverName = null, CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.Mcp.StartActivity("gantri.mcp.get_tools");

        if (serverName is not null)
        {
            if (!_servers.TryGetValue(serverName, out var server))
                throw new InvalidOperationException($"MCP server '{serverName}' not found.");

            await EnsureConnectedAsync(server, cancellationToken);
            return await server.DiscoverToolsAsync(cancellationToken);
        }

        var allTools = new List<McpToolInfo>();
        foreach (var server in _servers.Values)
        {
            await EnsureConnectedAsync(server, cancellationToken);
            if (server.IsConnected)
            {
                var tools = await server.DiscoverToolsAsync(cancellationToken);
                allTools.AddRange(tools);
            }
        }

        return allTools;
    }

    public async Task<McpToolResult> InvokeToolAsync(string serverName, string toolName,
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        using var activity = GantriActivitySources.Mcp.StartActivity("gantri.mcp.tool_call");
        activity?.SetTag(GantriSemanticConventions.McpServer, serverName);
        activity?.SetTag(GantriSemanticConventions.McpTool, toolName);

        if (!_servers.TryGetValue(serverName, out var server))
            return McpToolResult.Fail($"MCP server '{serverName}' not found.");

        await EnsureConnectedAsync(server, cancellationToken);

        GantriMeters.McpCallsTotal.Add(1,
            new KeyValuePair<string, object?>(GantriSemanticConventions.McpServer, serverName),
            new KeyValuePair<string, object?>(GantriSemanticConventions.McpTool, toolName));

        return await server.InvokeToolAsync(toolName, parameters, cancellationToken);
    }

    public IReadOnlyList<string> GetServerNames() => _servers.Keys.ToList();

    private async Task EnsureConnectedAsync(IMcpServer server, CancellationToken cancellationToken)
    {
        if (server.IsConnected) return;

        try
        {
            _logger.LogInformation("Auto-connecting MCP server '{Name}'...", server.Name);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            await server.ConnectAsync(cts.Token);
            _logger.LogInformation("Connected to MCP server '{Name}'", server.Name);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Timed out connecting to MCP server '{Name}' after 30s", server.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect MCP server '{Name}'", server.Name);
        }
    }
}
