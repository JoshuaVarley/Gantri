namespace Gantri.Plugins.Sdk.Connections;

/// <summary>
/// Represents an open, authenticated connection to an external service.
/// Managed by IConnectionProvider — plugins should NOT dispose these directly.
/// </summary>
public interface IServiceConnection : IAsyncDisposable
{
    string ProfileName { get; }
    string ServiceType { get; }
    string DisplayName { get; }
    bool IsValid { get; }
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
