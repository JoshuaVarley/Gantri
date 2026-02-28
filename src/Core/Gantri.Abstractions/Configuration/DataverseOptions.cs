namespace Gantri.Abstractions.Configuration;

public sealed class DataverseOptions
{
    public string? ActiveProfile { get; set; }
    public Dictionary<string, DataverseProfileOptions> Profiles { get; set; } = new();
    public bool CacheConnections { get; set; } = true;
}

public sealed class DataverseProfileOptions
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AuthType { get; set; } = "clientSecret";
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? Credential { get; set; }
    public string? CertificatePath { get; set; }
    public int TokenCacheDurationMinutes { get; set; } = 60;
}
