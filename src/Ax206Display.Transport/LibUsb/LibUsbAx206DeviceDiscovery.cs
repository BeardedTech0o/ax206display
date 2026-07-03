using Ax206Display.Transport.Discovery;
using LibUsbDotNet.LibUsb;

namespace Ax206Display.Transport.LibUsb;

/// <summary>
/// Enumerates every USB device visible to libusb and confirms which ones are
/// AX206 displays by claiming interface 0 and probing with GetLcdParameters -
/// see <see cref="IAx206DeviceDiscovery"/> for why this never filters by a
/// hardcoded VID/PID.
/// </summary>
public sealed class LibUsbAx206DeviceDiscovery : IAx206DeviceDiscovery, IDisposable
{
    private readonly UsbContext _context = new();

    public async Task<IReadOnlyList<IAx206Transport>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var discovered = new List<IAx206Transport>();

        using var devices = _context.List();
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var transport = await TryOpenAsDisplayAsync(device, cancellationToken);
            if (transport is not null)
            {
                discovered.Add(transport);
            }
        }

        return discovered;
    }

    private static async Task<IAx206Transport?> TryOpenAsDisplayAsync(IUsbDevice device, CancellationToken cancellationToken)
    {
        try
        {
            if (!device.TryOpen() || !device.ClaimInterface(0))
            {
                SafeClose(device);
                return null;
            }

            var deviceId = !string.IsNullOrEmpty(device.Info.SerialNumber)
                ? device.Info.SerialNumber
                : $"usb:{device.Info.VendorId:X4}:{device.Info.ProductId:X4}@{device.LocationId}";

            var transport = new LibUsbAx206Transport(device, deviceId);

            var parameters = await transport.GetLcdParametersAsync(cancellationToken);
            if (!parameters.HasPlausibleDimensions)
            {
                transport.Dispose();
                return null;
            }

            return transport;
        }
        catch (Exception)
        {
            // Expected for the many non-display USB devices on a typical machine
            // (hubs, keyboards, other vendor-private protocols that don't speak
            // this CBW/CSW dialect, devices busy with another driver, ...).
            SafeClose(device);
            return null;
        }
    }

    private static void SafeClose(IUsbDevice device)
    {
        try
        {
            if (device.IsOpen)
            {
                device.Close();
            }
        }
        catch (Exception)
        {
            // Best-effort cleanup of a device we're discarding anyway.
        }
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
