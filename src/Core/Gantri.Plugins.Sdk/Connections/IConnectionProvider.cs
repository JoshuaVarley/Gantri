namespace Gantri.Plugins.Sdk.Connections;

/// <summary>
/// Manages named connection profiles and a connection pool for a specific external service.
/// One provider per service type. Implement per-service marker interfaces
/// (e.g., IDataverseConnectionProvider) so plugins can resolve the specific provider they need.
/// </summary>
public interface IConnectionProvider
{
    string ServiceType { get; }
    IReadOnlyList<string> GetAvailableProfiles();
    string? GetActiveProfile();
    void SetActiveProfile(string profileName);
    Task<IServiceConnection> GetConnectionAsync(string profileName, CancellationToken ct = default);
    Task<IServiceConnection> GetActiveConnectionAsync(CancellationToken ct = default);
    Task<bool> TestConnectionAsync(string profileName, CancellationToken ct = default);
}
