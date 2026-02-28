using System.Collections.Concurrent;
using Gantri.Abstractions.Configuration;
using Gantri.Dataverse.Sdk;
using Gantri.Plugins.Sdk.Connections;
using Microsoft.Extensions.Logging;

namespace Gantri.Dataverse;

/// <summary>
/// Manages Dataverse connection profiles and a connection pool.
/// Implements <see cref="IDataverseConnectionProvider"/> for plugin resolution.
/// </summary>
internal sealed class DataverseConnectionProvider : IDataverseConnectionProvider
{
    private readonly DataverseOptions _options;
    private readonly DataverseTokenProvider _tokenProvider;
    private readonly ILogger<DataverseConnectionProvider> _logger;
    private readonly ConcurrentDictionary<string, DataverseServiceConnection> _connectionPool = new();
    private string? _activeProfile;

    public string ServiceType => "dataverse";

    public DataverseConnectionProvider(
        DataverseOptions options,
        DataverseTokenProvider tokenProvider,
        ILogger<DataverseConnectionProvider> logger)
    {
        _options = options;
        _tokenProvider = tokenProvider;
        _logger = logger;
        _activeProfile = options.ActiveProfile;
    }

    public IReadOnlyList<string> GetAvailableProfiles()
        => _options.Profiles.Keys.ToList();

    public string? GetActiveProfile() => _activeProfile;

    public void SetActiveProfile(string profileName)
    {
        if (!_options.Profiles.ContainsKey(profileName))
            throw new InvalidOperationException($"Profile '{profileName}' not found.");

        _activeProfile = profileName;
        _logger.LogInformation("Active Dataverse profile switched to '{Profile}'", profileName);
    }

    public async Task<IDataverseServiceConnection> GetConnectionAsync(string profileName, CancellationToken ct = default)
    {
        if (!_options.Profiles.TryGetValue(profileName, out var profile))
            throw new InvalidOperationException($"Dataverse profile '{profileName}' not found.");

        if (_options.CacheConnections &&
            _connectionPool.TryGetValue(profileName, out var existing) &&
            existing.IsValid)
        {
            return existing;
        }

        _logger.LogInformation("Creating new Dataverse connection for profile '{Profile}'", profileName);

        var client = _tokenProvider.CreateClient(profile);
        var connection = new DataverseServiceConnection(
            client,
            profileName,
            profile.Description ?? profileName,
            profile.Url);

        if (_options.CacheConnections)
            _connectionPool[profileName] = connection;

        return connection;
    }

    public async Task<IDataverseServiceConnection> GetActiveConnectionAsync(CancellationToken ct = default)
    {
        if (_activeProfile is null)
            throw new InvalidOperationException("No active Dataverse profile set. Configure 'active_profile' in dataverse.yaml or call SetActiveProfile().");

        return await GetConnectionAsync(_activeProfile, ct);
    }

    async Task<IServiceConnection> IConnectionProvider.GetConnectionAsync(string profileName, CancellationToken ct)
        => await GetConnectionAsync(profileName, ct);

    async Task<IServiceConnection> IConnectionProvider.GetActiveConnectionAsync(CancellationToken ct)
        => await GetActiveConnectionAsync(ct);

    public async Task<bool> TestConnectionAsync(string profileName, CancellationToken ct = default)
    {
        try
        {
            var connection = await GetConnectionAsync(profileName, ct);
            return connection.IsValid;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test failed for profile '{Profile}'", profileName);
            return false;
        }
    }
}
