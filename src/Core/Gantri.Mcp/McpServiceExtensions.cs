using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Gantri.Mcp;

public static class McpServiceExtensions
{
    public static IServiceCollection AddGantriMcp(this IServiceCollection services)
    {
        services.AddSingleton<McpClientManager>();
        services.AddSingleton<IMcpToolProvider>(sp => sp.GetRequiredService<McpClientManager>());
        services.AddSingleton<McpPermissionManager>();
        return services;
    }

    /// <summary>
    /// Registers configured MCP servers with the McpClientManager.
    /// Call post-build after config is loaded.
    /// </summary>
    public static void RegisterMcpServers(
        this McpClientManager manager,
        McpOptions? mcpOptions,
        ISecretResolver? secretResolver = null)
    {
        if (mcpOptions?.Servers is not { Count: > 0 })
            return;

        foreach (var (name, definition) in mcpOptions.Servers)
        {
            var server = secretResolver is not null
                ? new StdioMcpServer(name, definition, secretResolver)
                : new StdioMcpServer(name, definition);
            manager.RegisterServer(server);
        }
    }

    /// <summary>
    /// Registers per-agent MCP server permissions from loaded configuration.
    /// </summary>
    public static void RegisterMcpPermissions(
        this McpPermissionManager permissionManager,
        IReadOnlyDictionary<string, AgentDefinition>? agents
    )
    {
        if (agents is not { Count: > 0 })
            return;

        foreach (var (agentName, definition) in agents)
        {
            foreach (
                var serverName in definition
                    .McpServers.Where(static n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            )
            {
                permissionManager.AddAgentServer(agentName, serverName);
            }
        }
    }
}
