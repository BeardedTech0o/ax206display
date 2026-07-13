using Ax206Display.Config.Models;
using Ax206Display.Config.Services;
using Ax206Display.Rendering.Playback;
using Ax206Display.Rendering.Widgets;
using Ax206Display.Transport;
using Ax206Display.Transport.Discovery;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ax206Display.App.Services;

/// <summary>
/// The composition root's device orchestrator: discovers connected AX206
/// displays, matches each one against a saved <see cref="DeviceProfileConfig"/>
/// by <see cref="IAx206Transport.DeviceId"/>, auto-provisions a sane default
/// (full-screen clock) layout for any display seen for the first time so
/// plugging one in produces an immediate visible result, and runs one
/// <see cref="DeviceDisplayLoop"/> per display until the host stops.
/// </summary>
public sealed partial class DisplayManagerHostedService : IHostedService, IDisposable
{
    private static readonly TimeSpan MaxFrameInterval = TimeSpan.FromSeconds(1);

    private readonly IAx206DeviceDiscovery _discovery;
    private readonly ConfigService _configService;
    private readonly ILogger<DisplayManagerHostedService> _logger;
    private readonly List<IAx206Transport> _transports = [];
    private readonly List<Task> _loopTasks = [];
    private CancellationTokenSource? _loopCancellation;

    public DisplayManagerHostedService(IAx206DeviceDiscovery discovery, ConfigService configService, ILogger<DisplayManagerHostedService> logger)
    {
        _discovery = discovery;
        _configService = configService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = await _configService.LoadAsync(cancellationToken);
        var discovered = await _discovery.DiscoverAsync(cancellationToken);

        if (discovered.Count == 0)
        {
            LogNoDevicesFound();
            return;
        }

        _loopCancellation = new CancellationTokenSource();
        var configChanged = false;

        foreach (var transport in discovered)
        {
            _transports.Add(transport);

            var profile = config.Devices.FirstOrDefault(d => d.Id == transport.DeviceId);
            if (profile is null)
            {
                profile = await ProvisionDefaultProfileAsync(transport, cancellationToken);
                config = config with { Devices = [.. config.Devices, profile] };
                configChanged = true;
                LogAutoProvisioned(transport.DeviceId, profile.ScreenWidth, profile.ScreenHeight);
            }

            var placements = profile.Widgets
                .Select(w => new WidgetPlacement(WidgetFactory.Create(w), w.X, w.Y, w.ZOrder))
                .ToList();

            var interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, profile.TargetFps));
            if (interval > MaxFrameInterval)
            {
                interval = MaxFrameInterval;
            }

            var loop = new DeviceDisplayLoop(transport, placements, interval);
            _loopTasks.Add(RunLoopSafelyAsync(loop, transport.DeviceId, _loopCancellation.Token));
        }

        if (configChanged)
        {
            await _configService.SaveAsync(config, cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _loopCancellation?.Cancel();

        try
        {
            await Task.WhenAll(_loopTasks);
        }
        catch (Exception)
        {
            // Individual failures are already logged in RunLoopSafelyAsync;
            // this just keeps one bad device from failing host shutdown.
        }

        foreach (var transport in _transports)
        {
            transport.Dispose();
        }
    }

    private async Task RunLoopSafelyAsync(DeviceDisplayLoop loop, string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            await loop.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (Exception ex)
        {
            LogLoopFailed(ex, deviceId);
        }
    }

    public void Dispose()
    {
        _loopCancellation?.Dispose();
    }

    private static async Task<DeviceProfileConfig> ProvisionDefaultProfileAsync(IAx206Transport transport, CancellationToken cancellationToken)
    {
        var parameters = await transport.GetLcdParametersAsync(cancellationToken);

        return new DeviceProfileConfig
        {
            Id = transport.DeviceId,
            Name = $"Display ({transport.DeviceId})",
            Identity = new DeviceIdentity
            {
                // VendorId/ProductId aren't exposed through IAx206Transport
                // (only the composed DeviceId is) - matching here uses Id
                // (== transport.DeviceId), not Identity, until that's
                // threaded through discovery in a future milestone.
                VendorId = 0,
                ProductId = 0,
                SerialNumber = transport.DeviceId,
            },
            ScreenWidth = parameters.Width,
            ScreenHeight = parameters.Height,
            Widgets =
            [
                new WidgetConfig
                {
                    Id = "default-clock",
                    Type = "clock",
                    X = 0,
                    Y = 0,
                    Width = parameters.Width,
                    Height = parameters.Height,
                },
            ],
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No AX206 displays found.")]
    private partial void LogNoDevicesFound();

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-provisioned a default layout for {DeviceId} ({Width}x{Height}).")]
    private partial void LogAutoProvisioned(string deviceId, int width, int height);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Display loop for {DeviceId} stopped unexpectedly.")]
    private partial void LogLoopFailed(Exception exception, string deviceId);
}
