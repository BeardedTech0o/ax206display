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
    /// Like <see cref="DiscoverAsync(CancellationToken)"/>, but lets a caller
    /// that's only looking for one particular disconnected display tell
    /// discovery which device IDs are already connected and actively in use
    /// elsewhere, so it can leave the underlying physical USB devices alone
    /// rather than opening/claiming/probing them again. Opening a device a
    /// second time while its first handle is mid-blit can corrupt that
    /// handle's Bulk-Only-Transport CBW/CSW exchange (see
    /// docs/protocol-spec.md) and knock an otherwise-healthy display
    /// offline - see <see cref="Ax206Display.Transport.LibUsb.LibUsbAx206DeviceDiscovery"/> for the
    /// concrete case this closes. Implementations that can't tell a device's
    /// physical identity without opening it simply ignore
    /// <paramref name="excludeDeviceIds"/> and behave like
    /// <see cref="DiscoverAsync(CancellationToken)"/>.
    /// </summary>
    Task<IReadOnlyList<IAx206Transport>> DiscoverAsync(IReadOnlyCollection<string> excludeDeviceIds, CancellationToken cancellationToken = default)
        => DiscoverAsync(cancellationToken);

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
