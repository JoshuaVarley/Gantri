using Gantri.Plugins.Sdk.Connections;

namespace Gantri.Dataverse.Sdk;

/// <summary>
/// Dataverse-specific connection provider.
/// Plugins resolve via: context.Services.GetService&lt;IDataverseConnectionProvider&gt;()
/// Returns IDataverseServiceConnection instances with access to the underlying ServiceClient.
/// </summary>
public interface IDataverseConnectionProvider : IConnectionProvider
{
    /// <summary>Get a typed Dataverse connection to a specific profile.</summary>
    new Task<IDataverseServiceConnection> GetConnectionAsync(string profileName, CancellationToken ct = default);

    /// <summary>Get a typed Dataverse connection to the active profile.</summary>
    new Task<IDataverseServiceConnection> GetActiveConnectionAsync(CancellationToken ct = default);
}
