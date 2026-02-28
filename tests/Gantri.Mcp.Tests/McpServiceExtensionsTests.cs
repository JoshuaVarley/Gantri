using Gantri.Abstractions.Configuration;
using Gantri.Mcp;

namespace Gantri.Mcp.Tests;

public class McpServiceExtensionsTests
{
    [Fact]
    public void RegisterMcpPermissions_RegistersConfiguredAgentServers()
    {
        var agents = new Dictionary<string, AgentDefinition>
        {
            ["writer"] = new AgentDefinition
            {
                Name = "writer",
                McpServers = ["brave", "github", "brave"],
            },
            ["editor"] = new AgentDefinition { Name = "editor", McpServers = ["github"] },
        };

        var permissionManager = new McpPermissionManager();

        permissionManager.RegisterMcpPermissions(agents);

        permissionManager.IsAllowed("writer", "brave").Should().BeTrue();
        permissionManager.IsAllowed("writer", "github").Should().BeTrue();
        permissionManager.IsAllowed("writer", "nonexistent").Should().BeFalse();
        permissionManager.IsAllowed("editor", "github").Should().BeTrue();
        permissionManager.IsAllowed("editor", "brave").Should().BeFalse();

        permissionManager.GetAllowedServers("writer").Should().HaveCount(2);
    }
}
