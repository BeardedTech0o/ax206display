using System.Text.Json.Nodes;
using Ax206Display.Config.Models;
using Ax206Display.Config.Services;
using Ax206Display.DataSources.SystemMonitor;
using Ax206Display.Rendering.Playback;
using Ax206Display.Rendering.Widgets;
using Ax206Display.Transport;
using Ax206Display.Transport.Discovery;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

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
    private static readonly TimeSpan ConfigPollInterval = TimeSpan.FromSeconds(3);

    private readonly IAx206DeviceDiscovery _discovery;
    private readonly ConfigService _configService;
    private readonly IRenderDataProvider _dataProvider;
    private readonly ILogger<DisplayManagerHostedService> _logger;
    private readonly List<IAx206Transport> _transports = [];
    private readonly List<Task> _loopTasks = [];
    private readonly Dictionary<string, DeviceDisplayLoop> _loopsByDeviceId = [];
    private readonly Dictionary<string, string?> _backgroundImagePathsByDeviceId = [];
    private CancellationTokenSource? _loopCancellation;

    public DisplayManagerHostedService(
        IAx206DeviceDiscovery discovery,
        ConfigService configService,
        IRenderDataProvider dataProvider,
        ILogger<DisplayManagerHostedService> logger)
    {
        _discovery = discovery;
        _configService = configService;
        _dataProvider = dataProvider;
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

            var placements = BuildPlacements(transport.DeviceId, profile.Widgets);

            var interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, profile.TargetFps));
            if (interval > MaxFrameInterval)
            {
                interval = MaxFrameInterval;
            }

            var backgroundImage = LoadBackgroundImage(transport.DeviceId, profile.BackgroundImagePath);
            _backgroundImagePathsByDeviceId[transport.DeviceId] = profile.BackgroundImagePath;

            var loop = new DeviceDisplayLoop(transport, placements, interval, _dataProvider, backgroundImage);
            _loopsByDeviceId[transport.DeviceId] = loop;
            _loopTasks.Add(RunLoopSafelyAsync(loop, transport.DeviceId, _loopCancellation.Token));
        }

        if (configChanged)
        {
            await _configService.SaveAsync(config, cancellationToken);
        }

        // So layout edits made in the Widget Designer (or by hand-editing
        // config.json) reach the physical display without an app restart.
        _loopTasks.Add(WatchConfigForChangesAsync(_loopCancellation.Token));
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

    private async Task WatchConfigForChangesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ConfigPollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var config = await _configService.LoadAsync(cancellationToken);
                foreach (var (deviceId, loop) in _loopsByDeviceId)
                {
                    var profile = config.Devices.FirstOrDefault(d => d.Id == deviceId);
                    if (profile is null)
                    {
                        continue;
                    }

                    loop.UpdatePlacements(BuildPlacements(deviceId, profile.Widgets));

                    // Only re-decode the image when its path actually
                    // changed - re-reading and re-decoding an unchanged file
                    // every poll would be wasted work on every tick forever.
                    if (!_backgroundImagePathsByDeviceId.TryGetValue(deviceId, out var previousPath) || previousPath != profile.BackgroundImagePath)
                    {
                        loop.UpdateBackgroundImage(LoadBackgroundImage(deviceId, profile.BackgroundImagePath));
                        _backgroundImagePathsByDeviceId[deviceId] = profile.BackgroundImagePath;
                    }
                }
            }
            catch (Exception ex)
            {
                LogConfigReloadFailed(ex);
            }
        }
    }

    private SKBitmap? LoadBackgroundImage(string deviceId, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            // Decode returns null (rather than throwing) for a missing or
            // corrupt/unrecognized file - either way, fall back to the
            // plain background color instead of crashing the display loop.
            var bitmap = SKBitmap.Decode(path);
            if (bitmap is null)
            {
                LogBackgroundImageLoadFailed(null, deviceId, path);
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            LogBackgroundImageLoadFailed(ex, deviceId, path);
            return null;
        }
    }

    private List<WidgetPlacement> BuildPlacements(string deviceId, IEnumerable<WidgetConfig> widgets)
    {
        var placements = new List<WidgetPlacement>();

        foreach (var widget in widgets)
        {
            try
            {
                placements.Add(new WidgetPlacement(WidgetFactory.Create(widget), widget.X, widget.Y, widget.ZOrder));
            }
            catch (Exception ex)
            {
                LogWidgetLoadFailed(ex, widget.Id, deviceId);
            }
        }

        return placements;
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
            Widgets = BuildDefaultLayout(parameters.Width, parameters.Height),
        };
    }

    /// <summary>
    /// Clock across the top two thirds, live CPU load / CPU temp / RAM row
    /// underneath. Stats that this machine can't report (e.g. temperature
    /// without elevation) render as "--" rather than breaking the layout.
    /// </summary>
    private static List<WidgetConfig> BuildDefaultLayout(int width, int height)
    {
        var statRowHeight = height / 3;
        var clockHeight = height - statRowHeight;
        var statWidth = width / 3;

        return
        [
            new WidgetConfig
            {
                Id = "default-clock",
                Type = "clock",
                X = 0,
                Y = 0,
                Width = width,
                Height = clockHeight,
            },
            new WidgetConfig
            {
                Id = "default-cpu-load",
                Type = "stat",
                X = 0,
                Y = clockHeight,
                Width = statWidth,
                Height = statRowHeight,
                Settings = new JsonObject
                {
                    ["dataKey"] = SystemStatKeys.CpuLoadPercent,
                    ["label"] = "CPU",
                    ["unit"] = "%",
                },
            },
            new WidgetConfig
            {
                Id = "default-cpu-temp",
                Type = "stat",
                X = statWidth,
                Y = clockHeight,
                Width = statWidth,
                Height = statRowHeight,
                Settings = new JsonObject
                {
                    ["dataKey"] = SystemStatKeys.CpuTemperatureCelsius,
                    ["unit"] = "°C",
                },
            },
            new WidgetConfig
            {
                Id = "default-memory",
                Type = "stat",
                X = statWidth * 2,
                Y = clockHeight,
                Width = width - (statWidth * 2),
                Height = statRowHeight,
                Settings = new JsonObject
                {
                    ["dataKey"] = SystemStatKeys.MemoryUsedPercent,
                    ["label"] = "RAM",
                    ["unit"] = "%",
                },
            },
        ];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No AX206 displays found.")]
    private partial void LogNoDevicesFound();

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-provisioned a default layout for {DeviceId} ({Width}x{Height}).")]
    private partial void LogAutoProvisioned(string deviceId, int width, int height);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Display loop for {DeviceId} stopped unexpectedly.")]
    private partial void LogLoopFailed(Exception exception, string deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to reload widget config; keeping the previous layout on screen.")]
    private partial void LogConfigReloadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping widget {WidgetId} for {DeviceId} - failed to build it from saved config.")]
    private partial void LogWidgetLoadFailed(Exception exception, string widgetId, string deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load background image '{Path}' for {DeviceId}; using the plain background color instead.")]
    private partial void LogBackgroundImageLoadFailed(Exception? exception, string deviceId, string path);
}
