using Ax206Display.Protocol.Commands;
using Ax206Display.Transport;
using Ax206Display.Transport.Discovery;
using Ax206Display.Transport.LibUsb;

namespace Ax206Display.Server.Devices;

/// <summary>
/// Picks the device discovery the server runs with. Unlike the Windows tray
/// app, where a missing libusb is a fatal startup error with a dialog, the
/// server keeps running without it: the web UI is still how you'd find out
/// what's wrong, and layouts can be edited with no display attached.
/// AX206_DEMO=1 swaps in fake panels, for trying the UI with no hardware.
/// </summary>
internal static partial class UsbDiscoveryFactory
{
    public static IAx206DeviceDiscovery Create(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Ax206Display.Server.Devices");

        if (Environment.GetEnvironmentVariable("AX206_DEMO") == "1")
        {
            LogDemoMode(logger);
            return new DemoDeviceDiscovery();
        }

        try
        {
            return new LibUsbAx206DeviceDiscovery(services.GetRequiredService<ILogger<LibUsbAx206DeviceDiscovery>>());
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException)
        {
            LogLibUsbMissing(logger, ex);
            return new UnavailableDeviceDiscovery();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "libusb could not be loaded, so no displays can be driven. On Raspberry Pi OS / Debian: sudo apt install libusb-1.0-0, then restart the service.")]
    private static partial void LogLibUsbMissing(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AX206_DEMO=1: using two simulated displays instead of USB hardware.")]
    private static partial void LogDemoMode(ILogger logger);

    private sealed class UnavailableDeviceDiscovery : IAx206DeviceDiscovery
    {
        public Task<IReadOnlyList<IAx206Transport>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IAx206Transport>>([]);
    }

    /// <summary>Hands out fresh fake panels each scan, honoring the exclude list the way real discovery does.</summary>
    private sealed class DemoDeviceDiscovery : IAx206DeviceDiscovery
    {
        private static readonly (string Id, ushort Width, ushort Height)[] Panels =
        [
            ("DEMO-480x320", 480, 320),
            ("DEMO-320x480", 320, 480),
        ];

        public Task<IReadOnlyList<IAx206Transport>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            DiscoverAsync([], cancellationToken);

        public Task<IReadOnlyList<IAx206Transport>> DiscoverAsync(IReadOnlyCollection<string> excludeDeviceIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IAx206Transport>>(Panels
                .Where(p => !excludeDeviceIds.Contains(p.Id))
                .Select(p => (IAx206Transport)new DemoTransport(p.Id, p.Width, p.Height))
                .ToList());
    }

    private sealed class DemoTransport(string deviceId, ushort width, ushort height) : IAx206Transport
    {
        public string DeviceId { get; } = deviceId;

        public Task<LcdParametersResponse> GetLcdParametersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LcdParametersResponse(width, height, IsMarkerValid: true));

        public Task SetPropertyAsync(Ax206Property propertyToken, ushort value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BlitAsync(ushort left, ushort top, ushort right, ushort bottom, ReadOnlyMemory<byte> rgb565BigEndianPixels, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
