using Gantri.Plugins.Sdk.Connections;

namespace Gantri.Dataverse.Sdk;

/// <summary>
/// A Dataverse-specific service connection.
/// Extends the generic IServiceConnection with Dataverse-aware members.
/// </summary>
public interface IDataverseServiceConnection : IServiceConnection
{
    /// <summary>The Dataverse environment URL (e.g., https://org.crm.dynamics.com).</summary>
    string EnvironmentUrl { get; }

    /// <summary>The organization unique name.</summary>
    string? OrganizationName { get; }
}
