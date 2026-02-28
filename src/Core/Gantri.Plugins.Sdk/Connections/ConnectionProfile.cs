namespace Gantri.Plugins.Sdk.Connections;

/// <summary>
/// Minimal base model for a connection profile entry.
/// Service-specific profiles should extend this with their own fields
/// (e.g., DataverseConnectionProfile adds TenantId, AuthType, etc.).
/// </summary>
public class ConnectionProfile
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
}
