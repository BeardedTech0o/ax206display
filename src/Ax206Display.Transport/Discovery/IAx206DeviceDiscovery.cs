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
}
