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

    /// <summary>Proxmox login realm (e.g. "pam", "pve") - unused by integrations that don't have the concept.</summary>
    public string? Realm { get; init; }

    public string? SecretKey { get; init; }

    /// <summary>
    /// When set, TLS validation for this integration pins to this exact leaf
    /// certificate (SHA-256 thumbprint, hex, no separators) instead of running
    /// normal chain/hostname validation - i.e. certificate pinning, not a
    /// blanket "accept anything" bypass. Needed because UniFi/Proxmox
    /// controllers commonly serve a self-signed certificate on a LAN. Leave
    /// null to use normal system certificate validation. Consumed by
    /// Ax206Display.DataSources's IntegrationHttpClientFactory.
    /// </summary>
    public string? PinnedCertificateSha256Thumbprint { get; init; }
}
