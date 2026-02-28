using Gantri.Plugins.Sdk.Connections;

namespace Gantri.Dataverse.Sdk;

/// <summary>
/// Configuration for a single Dataverse environment connection.
/// Extends the generic ConnectionProfile with Dataverse/Azure AD-specific fields.
/// </summary>
public class DataverseConnectionProfile : ConnectionProfile
{
    /// <summary>
    /// Authentication type: "clientSecret", "deviceCode", "interactive", "azureCli", "certificate".
    /// </summary>
    public string AuthType { get; set; } = "clientSecret";

    /// <summary>Azure AD tenant ID (supports ${ENV_VAR} substitution).</summary>
    public string? TenantId { get; set; }

    /// <summary>Client/App registration ID for service principal or OAuth app.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret value (for clientSecret auth).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Certificate thumbprint or path (for certificate auth).</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Token cache duration in minutes (default: 60).</summary>
    public int TokenCacheDurationMinutes { get; set; } = 60;
}
