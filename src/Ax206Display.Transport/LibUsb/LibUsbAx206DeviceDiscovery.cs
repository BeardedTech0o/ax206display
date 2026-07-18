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

    private readonly AmbiguousSerialTracker _ambiguousSerialTracker = new();

    public LibUsbAx206DeviceDiscovery(ILogger<LibUsbAx206DeviceDiscovery>? logger = null)
    {
        _logger = logger ?? NullLogger<LibUsbAx206DeviceDiscovery>.Instance;
    }

    public async Task<IReadOnlyList<IAx206Transport>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var discovered = new List<LibUsbAx206Transport>();

        // Deliberately NOT disposed on the success path: disposing the
        // UsbDeviceCollection disposes every device in it (per LibUsbDotNet's
        // docs), which would close the handles behind the transports we are
        // about to hand out. Devices we keep are owned by their transport;
        // devices we reject are disposed individually below.
        var devices = _context.List();
        try
        {
            foreach (var device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var transport = await TryOpenAsDisplayAsync(device, cancellationToken);
                if (transport is not null)
                {
                    discovered.Add(transport);
                }
                else
                {
                    SafeDispose(device);
                }
            }
        }
        catch (Exception)
        {
            foreach (var transport in discovered)
            {
                transport.Dispose();
            }

            devices.Dispose();
            throw;
        }

        DisambiguateDuplicateSerialNumbers(discovered);
        return discovered;
    }

    /// <summary>
    /// Some generic/clone AX206 panels burn the same fixed placeholder string
    /// (often something that looks like a firmware build date) into every
    /// unit's USB serial-number descriptor instead of a true per-unit serial.
    /// Left alone, two such panels plugged in at once would collide onto the
    /// same DeviceId and only one would ever get a config entry. When that
    /// happens, disambiguate every colliding transport by appending its USB
    /// port location - stable for as long as that panel stays in the same
    /// physical port, but it does mean moving a disambiguated panel to a
    /// different port makes it look like a new device (there's no better
    /// identifier this hardware can offer). A serial seen colliding once
    /// stays disambiguated in every later scan too, via
    /// <see cref="_ambiguousSerialTracker"/> - see its doc comment for why.
    /// </summary>
    private void DisambiguateDuplicateSerialNumbers(List<LibUsbAx206Transport> discovered)
    {
        foreach (var group in discovered.GroupBy(t => t.DeviceId))
        {
            if (!_ambiguousSerialTracker.ShouldDisambiguate(group.Key, group.Count()))
            {
                continue;
            }

            foreach (var transport in group)
            {
                var disambiguated = $"{transport.DeviceId}@{transport.LocationId}";
                LogDuplicateSerialNumber(transport.DeviceId, disambiguated);
                transport.DeviceId = disambiguated;
            }
        }
    }

    private async Task<LibUsbAx206Transport?> TryOpenAsDisplayAsync(IUsbDevice device, CancellationToken cancellationToken)
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Multiple AX206 displays reported the same serial number '{DeviceId}' - disambiguating by USB port as '{DisambiguatedDeviceId}'.")]
    private partial void LogDuplicateSerialNumber(string deviceId, string disambiguatedDeviceId);

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

    private static void SafeDispose(IUsbDevice device)
    {
        try
        {
            device.Dispose();
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
