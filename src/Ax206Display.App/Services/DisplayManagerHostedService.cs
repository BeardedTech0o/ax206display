using System.Collections.Concurrent;
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
/// plugging one in produces an immediate visible result, and supervises one
/// <see cref="DeviceDisplayLoop"/> per display until the host stops -
/// including reconnecting a display whose USB connection drops mid-session
/// (see <see cref="SuperviseDeviceAsync"/>).
/// </summary>
public sealed partial class DisplayManagerHostedService : IHostedService, IDisposable
{
    private static readonly TimeSpan MaxFrameInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ConfigPollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);

    private readonly IAx206DeviceDiscovery _discovery;
    private readonly ConfigService _configService;
    private readonly IRenderDataProvider _dataProvider;
    private readonly ILogger<DisplayManagerHostedService> _logger;
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);
    private readonly List<Task> _loopTasks = [];

    // Concurrent: written and removed by each device's own
    // SuperviseDeviceAsync task as it connects/reconnects, while
    // WatchConfigForChangesAsync concurrently enumerates them on its own
    // polling task - a plain Dictionary isn't safe under that access pattern.
    private readonly ConcurrentDictionary<string, DeviceDisplayLoop> _loopsByDeviceId = new();
    private readonly ConcurrentDictionary<string, string?> _backgroundImagePathsByDeviceId = new();
    private readonly ConcurrentDictionary<string, int> _brightnessByDeviceId = new();

    // Tracks every deviceId with a live SuperviseDeviceAsync task, for the
    // task's entire lifetime (connected, reconnecting, or in between) - unlike
    // _loopsByDeviceId, which only holds an entry while that device's loop is
    // actively blitting. RefreshDevicesAsync uses this (via TryAdd, so two
    // concurrent refreshes can't both claim the same newly-found device) to
    // tell "already being supervised" apart from "genuinely new".
    private readonly ConcurrentDictionary<string, byte> _supervisedDeviceIds = new();
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
        _loopCancellation = new CancellationTokenSource();

        var config = await _configService.LoadAsync(cancellationToken);
        var discovered = await DiscoverSafelyAsync(cancellationToken);

        if (discovered.Count == 0)
        {
            LogNoDevicesFound();
        }
        else
        {
            var configChanged = false;

            foreach (var transport in discovered)
            {
                var profile = config.Devices.FirstOrDefault(d => d.Id == transport.DeviceId);
                if (profile is null)
                {
                    try
                    {
                        profile = await ProvisionDefaultProfileAsync(transport, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // A transient USB failure here (e.g. GetLcdParameters
                        // timing out) must not take down the whole host - it
                        // would silently kill every other already-working
                        // display along with it. Skip this device; it'll be
                        // picked up on the next RefreshDevicesAsync or app
                        // restart instead.
                        LogProvisioningFailed(ex, transport.DeviceId);
                        transport.Dispose();
                        continue;
                    }

                    config = config with { Devices = [.. config.Devices, profile] };
                    configChanged = true;
                    LogAutoProvisioned(transport.DeviceId, profile.ScreenWidth, profile.ScreenHeight);
                }

                _supervisedDeviceIds.TryAdd(transport.DeviceId, 0);
                _loopTasks.Add(SuperviseDeviceAsync(transport.DeviceId, transport, _loopCancellation.Token));
            }

            if (configChanged)
            {
                await _configService.SaveAsync(config, cancellationToken);
            }
        }

        // So layout edits made in the Widget Designer (or by hand-editing
        // config.json) reach the physical display without an app restart.
        // Started unconditionally, even with zero devices found here, so a
        // device adopted later via RefreshDevicesAsync gets hot-reload
        // immediately instead of needing special-casing there.
        _loopTasks.Add(WatchConfigForChangesAsync(_loopCancellation.Token));
    }

    /// <summary>
    /// Runs discovery again and starts supervising any display that isn't
    /// already being handled (a genuinely new plug-in, or one this process
    /// has never seen - <see cref="StartAsync"/> only scans once at launch).
    /// Auto-provisions a default layout for any device with no saved profile
    /// yet, same as StartAsync does. Returns how many new devices were
    /// picked up, for the Widget Designer's Refresh button to report back.
    /// Safe to call before StartAsync has found any device (i.e. the
    /// "Connect a display and reopen this window" case): it starts
    /// supervising immediately rather than requiring an app restart.
    /// </summary>
    public async Task<int> RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        _loopCancellation ??= new CancellationTokenSource();

        var discovered = await DiscoverSafelyAsync(cancellationToken);
        var config = await _configService.LoadAsync(cancellationToken);
        var configChanged = false;
        var newDeviceCount = 0;

        foreach (var transport in discovered)
        {
            if (!_supervisedDeviceIds.TryAdd(transport.DeviceId, 0))
            {
                // Already has a live supervisor task (connected or between
                // reconnect attempts) - this handle isn't needed.
                transport.Dispose();
                continue;
            }

            var profile = config.Devices.FirstOrDefault(d => d.Id == transport.DeviceId);
            if (profile is null)
            {
                try
                {
                    profile = await ProvisionDefaultProfileAsync(transport, cancellationToken);
                }
                catch (Exception ex)
                {
                    LogProvisioningFailed(ex, transport.DeviceId);
                    _supervisedDeviceIds.TryRemove(transport.DeviceId, out _);
                    transport.Dispose();
                    continue;
                }

                config = config with { Devices = [.. config.Devices, profile] };
                configChanged = true;
                LogAutoProvisioned(transport.DeviceId, profile.ScreenWidth, profile.ScreenHeight);
            }

            _loopTasks.Add(SuperviseDeviceAsync(transport.DeviceId, transport, _loopCancellation.Token));
            newDeviceCount++;
        }

        if (configChanged)
        {
            await _configService.SaveAsync(config, cancellationToken);
        }

        return newDeviceCount;
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
            // Individual failures are already logged inside
            // SuperviseDeviceAsync; this just keeps one bad device from
            // failing host shutdown. Each supervisor disposes its own
            // current transport as it exits, so there's nothing left to
            // clean up here.
        }
    }

    /// <summary>
    /// Owns one device slot for the lifetime of the host: runs its display
    /// loop, and if that loop ever ends abnormally (a USB disconnect mid-
    /// blit is the common case - see docs on IAx206Transport.BlitAsync),
    /// disposes the dead transport, waits, and re-runs discovery looking
    /// for the same <paramref name="deviceId"/> again so the display
    /// resumes without an app restart. Exits only on host shutdown or if
    /// the device's profile is removed from config entirely.
    /// </summary>
    private async Task SuperviseDeviceAsync(string deviceId, IAx206Transport? initialTransport, CancellationToken cancellationToken)
    {
        try
        {
            await SuperviseDeviceLoopAsync(deviceId, initialTransport, cancellationToken);
        }
        finally
        {
            // Releases this deviceId back for RefreshDevicesAsync to pick up
            // fresh - only reached once this task exits for good (host
            // shutdown, or the device's profile was removed from config),
            // never on an ordinary disconnect/reconnect cycle.
            _supervisedDeviceIds.TryRemove(deviceId, out _);
        }
    }

    private async Task SuperviseDeviceLoopAsync(string deviceId, IAx206Transport? initialTransport, CancellationToken cancellationToken)
    {
        var transport = initialTransport;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (transport is null)
            {
                transport = await TryFindTransportAsync(deviceId, cancellationToken);
                if (transport is null)
                {
                    if (!await DelaySafelyAsync(ReconnectInterval, cancellationToken))
                    {
                        break;
                    }

                    continue;
                }

                LogDeviceReconnected(deviceId);
            }

            var shouldReconnect = true;
            try
            {
                var config = await _configService.LoadAsync(cancellationToken);
                var profile = config.Devices.FirstOrDefault(d => d.Id == deviceId);
                if (profile is null)
                {
                    shouldReconnect = false;
                }
                else
                {
                    var placements = BuildPlacements(deviceId, profile.Widgets);
                    var backgroundImage = LoadBackgroundImage(deviceId, profile.BackgroundImagePath);
                    _backgroundImagePathsByDeviceId[deviceId] = profile.BackgroundImagePath;
                    _brightnessByDeviceId[deviceId] = profile.Brightness;

                    var loop = new DeviceDisplayLoop(transport, placements, ComputeInterval(profile.TargetFps), _dataProvider, backgroundImage);
                    _loopsByDeviceId[deviceId] = loop;

                    await loop.SetBrightnessAsync(profile.Brightness, cancellationToken);
                    await loop.RunAsync(cancellationToken);

                    // RunAsync only returns (rather than throwing) when its
                    // own cancellation check ends the loop - i.e. host
                    // shutdown, not a device failure. Nothing to reconnect.
                    shouldReconnect = false;
                }
            }
            catch (OperationCanceledException)
            {
                shouldReconnect = false;
            }
            catch (Exception ex)
            {
                LogLoopFailed(ex, deviceId);
            }
            finally
            {
                _loopsByDeviceId.TryRemove(deviceId, out _);
                transport?.Dispose();
                transport = null;
            }

            if (!shouldReconnect || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            LogReconnecting(deviceId);
            if (!await DelaySafelyAsync(ReconnectInterval, cancellationToken))
            {
                break;
            }
        }
    }

    private async Task<IAx206Transport?> TryFindTransportAsync(string deviceId, CancellationToken cancellationToken)
    {
        var discovered = await DiscoverSafelyAsync(cancellationToken);

        IAx206Transport? match = null;
        foreach (var transport in discovered)
        {
            if (match is null && transport.DeviceId == deviceId)
            {
                match = transport;
            }
            else
            {
                // Either a duplicate match (shouldn't happen - device IDs
                // are meant to be unique) or a different device this
                // supervisor isn't responsible for - don't leak its handle.
                transport.Dispose();
            }
        }

        return match;
    }

    /// <summary>
    /// Serializes every discovery scan across all device supervisors -
    /// concurrent reconnect attempts (e.g. two displays dropping around the
    /// same time) would otherwise race to open/claim the same USB devices.
    /// </summary>
    private async Task<IReadOnlyList<IAx206Transport>> DiscoverSafelyAsync(CancellationToken cancellationToken)
    {
        await _discoveryLock.WaitAsync(cancellationToken);
        try
        {
            return await _discovery.DiscoverAsync(cancellationToken);
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    private static async Task<bool> DelaySafelyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static TimeSpan ComputeInterval(int targetFps)
    {
        var interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, targetFps));
        return interval > MaxFrameInterval ? MaxFrameInterval : interval;
    }

    private async Task WatchConfigForChangesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await DelaySafelyAsync(ConfigPollInterval, cancellationToken))
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

                    // Same idea: only send the USB property write when the
                    // saved value actually changed, not on every poll tick.
                    if (!_brightnessByDeviceId.TryGetValue(deviceId, out var previousBrightness) || previousBrightness != profile.Brightness)
                    {
                        await loop.SetBrightnessAsync(profile.Brightness, cancellationToken);
                        _brightnessByDeviceId[deviceId] = profile.Brightness;
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

    public void Dispose()
    {
        _loopCancellation?.Dispose();
        _discoveryLock.Dispose();
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to auto-provision a default layout for {DeviceId}; skipping it for now.")]
    private partial void LogProvisioningFailed(Exception exception, string deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Display loop for {DeviceId} stopped unexpectedly; will try to reconnect.")]
    private partial void LogLoopFailed(Exception exception, string deviceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Attempting to reconnect {DeviceId}...")]
    private partial void LogReconnecting(string deviceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconnected {DeviceId}.")]
    private partial void LogDeviceReconnected(string deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to reload widget config; keeping the previous layout on screen.")]
    private partial void LogConfigReloadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping widget {WidgetId} for {DeviceId} - failed to build it from saved config.")]
    private partial void LogWidgetLoadFailed(Exception exception, string widgetId, string deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load background image '{Path}' for {DeviceId}; using the plain background color instead.")]
    private partial void LogBackgroundImageLoadFailed(Exception? exception, string deviceId, string path);
}
