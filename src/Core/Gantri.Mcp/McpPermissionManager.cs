using Gantri.Abstractions.Mcp;

namespace Gantri.Mcp;

public sealed class McpPermissionManager
{
    private readonly HashSet<string> _globalServers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _perAgentServers = new(StringComparer.OrdinalIgnoreCase);

    public void AddGlobalServer(string serverName)
    {
        _globalServers.Add(serverName);
    }

    public void AddAgentServer(string agentName, string serverName)
    {
        if (!_perAgentServers.TryGetValue(agentName, out var servers))
        {
            servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _perAgentServers[agentName] = servers;
        }

        servers.Add(serverName);
    }

    public IReadOnlyList<string> GetAllowedServers(string agentName)
    {
        var allowed = new HashSet<string>(_globalServers, StringComparer.OrdinalIgnoreCase);

        if (_perAgentServers.TryGetValue(agentName, out var agentServers))
        {
            foreach (var server in agentServers)
                allowed.Add(server);
        }

        return allowed.ToList();
    }

    public bool IsAllowed(string agentName, string serverName)
    {
        if (_globalServers.Contains(serverName))
            return true;

        return _perAgentServers.TryGetValue(agentName, out var servers) && servers.Contains(serverName);
    }
}
