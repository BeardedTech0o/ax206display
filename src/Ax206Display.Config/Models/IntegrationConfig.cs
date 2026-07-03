namespace Ax206Display.Config.Models;

/// <summary>
/// Connection settings for an external API integration (UniFi, Proxmox, ...).
/// The password/API token itself is never stored here - <see cref="SecretKey"/>
/// points to an entry in the DPAPI-protected secret store.
/// </summary>
public sealed record IntegrationConfig
{
    public required string Id { get; init; }

    public required string Kind { get; init; }

    public required string BaseUrl { get; init; }

    public string? Username { get; init; }

    public string? SecretKey { get; init; }

    public bool AllowInvalidTlsCertificate { get; init; }
}
