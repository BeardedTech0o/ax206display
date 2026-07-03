using Ax206Display.Transport.Discovery;
using LibUsbDotNet.LibUsb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ax206Display.Transport.LibUsb;

/// <summary>
/// Enumerates every USB device visible to libusb and confirms which ones are
/// AX206 displays by claiming interface 0 and probing with GetLcdParameters -
/// see <see cref="IAx206DeviceDiscovery"/> for why this never filters by a
/// hardcoded VID/PID.
/// </summary>
public sealed partial class LibUsbAx206DeviceDiscovery : IAx206DeviceDiscovery, IDisposable
{
    private readonly UsbContext _context = new();
    private readonly ILogger<LibUsbAx206DeviceDiscovery> _logger;

    public LibUsbAx206DeviceDiscovery(ILogger<LibUsbAx206DeviceDiscovery>? logger = null)
    {
        _logger = logger ?? NullLogger<LibUsbAx206DeviceDiscovery>.Instance;
    }

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

    private async Task<IAx206Transport?> TryOpenAsDisplayAsync(IUsbDevice device, CancellationToken cancellationToken)
    {
        try
        {
            if (!device.TryOpen() || !device.ClaimInterface(0))
            {
                LogSkippedUnclaimable(device.Info.VendorId, device.Info.ProductId);
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
                LogImplausibleResponse(deviceId, parameters.Width, parameters.Height);
                transport.Dispose();
                return null;
            }

            LogDisplayFound(deviceId, parameters.Width, parameters.Height);
            return transport;
        }
        catch (Exception ex)
        {
            // Expected for the many non-display USB devices on a typical machine
            // (hubs, keyboards, other vendor-private protocols that don't speak
            // this CBW/CSW dialect, devices busy with another driver, ...) - logged
            // at Warning rather than escalated so discovery keeps scanning, but the
            // rejection is still visible for later troubleshooting.
            LogRejectedDuringProbe(ex, device.Info.VendorId, device.Info.ProductId);
            SafeClose(device);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping USB device {VendorId:X4}:{ProductId:X4} - could not open or claim interface 0.")]
    private partial void LogSkippedUnclaimable(ushort vendorId, ushort productId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rejecting USB device {DeviceId} - implausible GetLcdParameters response ({Width}x{Height}).")]
    private partial void LogImplausibleResponse(string deviceId, ushort width, ushort height);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found AX206 display {DeviceId} ({Width}x{Height}).")]
    private partial void LogDisplayFound(string deviceId, ushort width, ushort height);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejecting USB device {VendorId:X4}:{ProductId:X4} during AX206 discovery.")]
    private partial void LogRejectedDuringProbe(Exception exception, ushort vendorId, ushort productId);

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
