namespace Ax206Display.Transport.LibUsb;

/// <summary>
/// Remembers which physical USB location each device ID was last found at,
/// so a discovery scan can tell "this raw USB device is the one already
/// driving display X" without opening it - see
/// <see cref="LibUsbAx206DeviceDiscovery"/>'s use of this for why that
/// matters. Generic over the location type only so this logic (and its
/// tests) don't need to depend on LibUsbDotNet's <c>LocationId</c> struct.
/// </summary>
public sealed class ActiveDeviceLocationTracker<TLocation> where TLocation : notnull
{
    private readonly Dictionary<string, TLocation> _locationByDeviceId = [];

    /// <summary>Records (or updates) where <paramref name="deviceId"/> was found in the scan that just finished.</summary>
    public void Remember(string deviceId, TLocation location)
    {
        _locationByDeviceId[deviceId] = location;
    }

    /// <summary>
    /// True if <paramref name="location"/> is the last-known location of any
    /// of <paramref name="deviceIds"/> - i.e. this physical device is
    /// (or very recently was) already claimed for one of them, and a new
    /// scan should leave it alone rather than opening/claiming/probing it
    /// again.
    /// </summary>
    public bool IsKnownLocationOf(TLocation location, IEnumerable<string> deviceIds)
    {
        foreach (var deviceId in deviceIds)
        {
            if (_locationByDeviceId.TryGetValue(deviceId, out var knownLocation) && EqualityComparer<TLocation>.Default.Equals(knownLocation, location))
            {
                return true;
            }
        }

        return false;
    }
}
