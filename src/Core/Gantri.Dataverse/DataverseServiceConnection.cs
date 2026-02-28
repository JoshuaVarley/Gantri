using Microsoft.PowerPlatform.Dataverse.Client;
using Gantri.Dataverse.Sdk;

namespace Gantri.Dataverse;

internal sealed class DataverseServiceConnection : IDataverseServiceConnection
{
    private readonly ServiceClient _client;

    public string ProfileName { get; }
    public string ServiceType => "dataverse";
    public string DisplayName { get; }
    public string EnvironmentUrl { get; }
    public string? OrganizationName => _client.ConnectedOrgUniqueName;
    public bool IsValid => _client.IsReady;

    internal DataverseServiceConnection(
        ServiceClient client,
        string profileName,
        string displayName,
        string environmentUrl)
    {
        _client = client;
        ProfileName = profileName;
        DisplayName = displayName;
        EnvironmentUrl = environmentUrl;
    }

    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    // ServiceClient handles token refresh internally on each call

    /// <summary>Access the underlying ServiceClient for D365 SDK operations.</summary>
    internal ServiceClient Client => _client;

    public async ValueTask DisposeAsync() => _client.Dispose();
}
