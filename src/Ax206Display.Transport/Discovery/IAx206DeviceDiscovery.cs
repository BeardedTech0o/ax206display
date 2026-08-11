namespace Ax206Display.Transport.Discovery;

/// <summary>
/// Finds connected AX206 displays. Implementations must confirm a candidate
/// USB device is really an AX206 display by probing it with the protocol's
/// GetLcdParameters command (see docs/protocol-spec.md) rather than filtering
/// by a hardcoded VID/PID, so unlisted/rebadged clones are still discovered.
/// </summary>
public interface IAx206DeviceDiscovery
{
    Task<IReadOnlyList<IAx206Transport>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Primes this discovery's duplicate-serial disambiguation memory with
    /// serial numbers already known (from a previous run) to collide across
    /// two physical panels, so a restart that only enumerates one of them in
    /// its first scan still disambiguates it instead of reporting it as an
    /// unrecognized new device. No-op for implementations that don't
    /// disambiguate by serial collision.
    /// </summary>
    void SeedKnownAmbiguousSerialNumbers(IEnumerable<string> serialNumbers)
    {
    }
}
