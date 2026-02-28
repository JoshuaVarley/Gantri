using System.Text.RegularExpressions;
using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Gantri.Mcp;

public sealed partial class StdioMcpServer : IMcpServer
{
    private readonly McpServerDefinition _definition;
    private readonly ISecretResolver _secretResolver;
    private IMcpClient? _client;

    public StdioMcpServer(string name, McpServerDefinition definition)
        : this(name, definition, DefaultSecretResolver.Instance)
    {
    }

    public StdioMcpServer(string name, McpServerDefinition definition, ISecretResolver secretResolver)
    {
        Name = name;
        _definition = definition;
        _secretResolver = secretResolver;
    }

    /// <summary>
    /// Default fallback that reads from environment variables only.
    /// Avoids a hard dependency on Gantri.Configuration from the Mcp project.
    /// </summary>
    private sealed class DefaultSecretResolver : ISecretResolver
    {
        public static readonly DefaultSecretResolver Instance = new();
        public string? Resolve(string key) => Environment.GetEnvironmentVariable(key);
    }

    public string Name { get; }
    public string Transport => _definition.Transport;
    public bool IsConnected => _client is not null;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null) return;

        var env = ResolveEnvironmentVariables(_definition.Env);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = _definition.Command,
            Arguments = _definition.Args,
            EnvironmentVariables = env!
        });

        _client = await McpClientFactory.CreateAsync(
            transport,
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "gantri", Version = "0.1.0" }
            },
            cancellationToken: cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }

    public async Task<IReadOnlyList<McpToolInfo>> DiscoverToolsAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
            throw new InvalidOperationException($"MCP server '{Name}' is not connected.");

        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);

        return tools.Select(t => new McpToolInfo
        {
            ServerName = Name,
            ToolName = t.Name,
            Description = t.Description,
            InputSchema = t.JsonSchema.ToString()
        }).ToList();
    }

    public async Task<McpToolResult> InvokeToolAsync(string toolName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return McpToolResult.Fail($"MCP server '{Name}' is not connected.");

        try
        {
            var args = parameters?.ToDictionary(kv => kv.Key, kv => kv.Value);
            var result = await _client.CallToolAsync(toolName, args, cancellationToken: cancellationToken);
            var textParts = result.Content
                .Where(c => c.Text is not null)
                .Select(c => c.Text!);
            var text = string.Join("\n", textParts);
            return McpToolResult.Ok(text);
        }
        catch (Exception ex)
        {
            return McpToolResult.Fail(ex.Message);
        }
    }

    private Dictionary<string, string> ResolveEnvironmentVariables(Dictionary<string, string> env)
    {
        var resolved = new Dictionary<string, string>(env.Count);
        foreach (var (key, value) in env)
        {
            resolved[key] = EnvVarPattern().Replace(value, match =>
            {
                var varName = match.Groups[1].Value;
                return _secretResolver.Resolve(varName) ?? string.Empty;
            });
        }
        return resolved;
    }

    [GeneratedRegex(@"\$\{(\w+)\}")]
    private static partial Regex EnvVarPattern();
}
