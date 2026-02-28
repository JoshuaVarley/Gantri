using Azure.Core;
using Azure.Identity;
using Gantri.Abstractions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.PowerPlatform.Dataverse.Client.Model;

namespace Gantri.Dataverse;

/// <summary>
/// Creates <see cref="ServiceClient"/> instances using the appropriate auth strategy
/// based on <see cref="DataverseProfileOptions.AuthType"/>.
/// Uses <see cref="ConnectionOptions"/> with external token management to bridge
/// Azure.Identity credentials into the Dataverse ServiceClient.
/// </summary>
internal sealed class DataverseTokenProvider
{
    private readonly ILogger<DataverseTokenProvider> _logger;

    public DataverseTokenProvider(ILogger<DataverseTokenProvider> logger)
    {
        _logger = logger;
    }

    public ServiceClient CreateClient(DataverseProfileOptions profile)
    {
        _logger.LogInformation(
            "Creating Dataverse ServiceClient for {Url} using {AuthType} auth",
            profile.Url,
            profile.AuthType);

        return profile.AuthType.ToLowerInvariant() switch
        {
            "clientsecret" or "client_secret" => CreateWithClientSecret(profile),
            "devicecode" or "device_code" => CreateWithDeviceCode(profile),
            "interactive" => CreateWithInteractive(profile),
            "azurecli" or "azure_cli" => CreateWithAzureCli(profile),
            "certificate" => CreateWithCertificate(profile),
            _ => throw new InvalidOperationException(
                $"Unsupported auth type '{profile.AuthType}'. " +
                "Supported: clientSecret, deviceCode, interactive, azureCli, certificate")
        };
    }

    private static ServiceClient CreateWithClientSecret(DataverseProfileOptions profile)
    {
        if (string.IsNullOrEmpty(profile.ClientId))
            throw new InvalidOperationException("ClientId is required for clientSecret auth.");
        if (string.IsNullOrEmpty(profile.Credential))
            throw new InvalidOperationException("Credential (client secret) is required for clientSecret auth.");

        var connectionString =
            $"AuthType=ClientSecret;" +
            $"Url={profile.Url};" +
            $"ClientId={profile.ClientId};" +
            $"ClientSecret={profile.Credential}";

        return new ServiceClient(connectionString);
    }

    private static ServiceClient CreateWithDeviceCode(DataverseProfileOptions profile)
    {
        if (string.IsNullOrEmpty(profile.TenantId))
            throw new InvalidOperationException("TenantId is required for deviceCode auth.");

        var credential = new DeviceCodeCredential(new DeviceCodeCredentialOptions
        {
            TenantId = profile.TenantId,
            ClientId = profile.ClientId ?? "51f81489-12ee-4a9e-aaae-a2591f45987d",
            DeviceCodeCallback = (code, _) =>
            {
                Console.Error.WriteLine(code.Message);
                return Task.CompletedTask;
            }
        });

        return CreateWithTokenCredential(profile, credential);
    }

    private static ServiceClient CreateWithInteractive(DataverseProfileOptions profile)
    {
        if (string.IsNullOrEmpty(profile.TenantId))
            throw new InvalidOperationException("TenantId is required for interactive auth.");

        var credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            TenantId = profile.TenantId,
            ClientId = profile.ClientId ?? "51f81489-12ee-4a9e-aaae-a2591f45987d"
        });

        return CreateWithTokenCredential(profile, credential);
    }

    private static ServiceClient CreateWithAzureCli(DataverseProfileOptions profile)
    {
        var credential = new AzureCliCredential();
        return CreateWithTokenCredential(profile, credential);
    }

    private static ServiceClient CreateWithCertificate(DataverseProfileOptions profile)
    {
        if (string.IsNullOrEmpty(profile.TenantId))
            throw new InvalidOperationException("TenantId is required for certificate auth.");
        if (string.IsNullOrEmpty(profile.ClientId))
            throw new InvalidOperationException("ClientId is required for certificate auth.");
        if (string.IsNullOrEmpty(profile.CertificatePath))
            throw new InvalidOperationException("CertificatePath is required for certificate auth.");

        var connectionString =
            $"AuthType=Certificate;" +
            $"Url={profile.Url};" +
            $"ClientId={profile.ClientId};" +
            $"Thumbprint={profile.CertificatePath}";

        return new ServiceClient(connectionString);
    }

    /// <summary>
    /// Creates a ServiceClient using external token management, bridging an Azure.Identity
    /// <see cref="TokenCredential"/> into the ServiceClient's token provider callback.
    /// </summary>
    private static ServiceClient CreateWithTokenCredential(DataverseProfileOptions profile, TokenCredential credential)
    {
        var serviceUri = new Uri(profile.Url);
        var options = new ConnectionOptions
        {
            AuthenticationType = AuthenticationType.ExternalTokenManagement,
            ServiceUri = serviceUri,
            AccessTokenProviderFunctionAsync = async (instanceUrl) =>
            {
                var scope = instanceUrl.TrimEnd('/') + "/.default";
                var tokenRequest = new TokenRequestContext([scope]);
                var token = await credential.GetTokenAsync(tokenRequest, CancellationToken.None);
                return token.Token;
            }
        };

        return new ServiceClient(options);
    }
}
