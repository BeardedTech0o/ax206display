using System.Diagnostics;
using Ax206Display.Protocol.Commands;
using Ax206Display.Rendering.Compositing;
using Ax206Display.Rendering.PixelFormats;
using Ax206Display.Rendering.Widgets;
using Ax206Display.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Ax206Display.Rendering.Playback;

/// <summary>
/// Drives one physical display: queries its real resolution (never trusts a
/// stored config value - see docs/protocol-spec.md on not hardcoding display
/// properties), then composes and blits frames on a timer until cancelled.
/// </summary>
public sealed partial class DeviceDisplayLoop
{
    private static readonly IReadOnlyDictionary<string, object> EmptyData = new Dictionary<string, object>();

    /// <summary>
    /// How long an unchanged picture is left alone before it's sent again
    /// anyway. A panel keeps showing what it was last given, so re-sending
    /// identical pixels only costs USB time - but a panel that reset itself
    /// without the host noticing would otherwise sit on its splash screen
    /// until the layout next changed.
    /// </summary>
    public static readonly TimeSpan DefaultKeepAliveInterval = TimeSpan.FromSeconds(30);

    private readonly IAx206Transport _transport;
    private IReadOnlyList<WidgetPlacement> _placements;
    private SKBitmap? _backgroundImage;
    private readonly TimeSpan _interval;
    private readonly IRenderDataProvider? _dataProvider;
    private readonly ILogger<DeviceDisplayLoop> _logger;
    private readonly SemaphoreSlim? _transferGate;
    private readonly TimeSpan _keepAliveInterval;

    public DeviceDisplayLoop(
        IAx206Transport transport,
        IReadOnlyList<WidgetPlacement> placements,
        TimeSpan interval,
        IRenderDataProvider? dataProvider = null,
        SKBitmap? backgroundImage = null,
        ILogger<DeviceDisplayLoop>? logger = null,
        SemaphoreSlim? transferGate = null,
        TimeSpan? keepAliveInterval = null)
    {
        _transport = transport;
        _placements = placements;
        _interval = interval;
        _dataProvider = dataProvider;
        _backgroundImage = backgroundImage;
        _logger = logger ?? NullLogger<DeviceDisplayLoop>.Instance;
        _transferGate = transferGate;
        _keepAliveInterval = keepAliveInterval ?? DefaultKeepAliveInterval;
    }

    /// <summary>
    /// Swaps in a new layout for this device without restarting the loop -
    /// used when a saved config edit (e.g. from the Widget Designer) should
    /// take effect live. Safe to call from any thread.
    /// </summary>
    public void UpdatePlacements(IReadOnlyList<WidgetPlacement> placements)
    {
        Volatile.Write(ref _placements, placements);
    }

    /// <summary>
    /// Swaps in a new (or null) background image without restarting the
    /// loop. The previous bitmap is deliberately not disposed here - the
    /// render loop may be mid-frame with a reference to it on another
    /// thread; it becomes unreferenced and is reclaimed by the GC instead.
    /// Safe to call from any thread.
    /// </summary>
    public void UpdateBackgroundImage(SKBitmap? backgroundImage)
    {
        Volatile.Write(ref _backgroundImage, backgroundImage);
    }

    /// <summary>
    /// Sends a hardware backlight level directly to the transport (0-7, per
    /// docs/protocol-spec.md). This is a one-off SetProperty command, not
    /// part of the per-frame blit path, so - unlike placements/background -
    /// there's nothing to swap in on the next frame; it takes effect on the
    /// display as soon as the device processes it.
    /// </summary>
    public Task SetBrightnessAsync(int brightness, CancellationToken cancellationToken = default) =>
        _transport.SetPropertyAsync(Ax206Property.Brightness, (ushort)Math.Clamp(brightness, 0, 7), cancellationToken);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var parameters = await _transport.GetLcdParametersAsync(cancellationToken);
        LogLoopStarted(_transport.DeviceId, parameters.Width, parameters.Height);

        var compositor = new FrameCompositor(parameters.Width, parameters.Height);

        byte[]? lastSent = null;
        long lastSentAt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var context = new WidgetRenderContext
            {
                Now = DateTimeOffset.Now,
                Data = _dataProvider?.GetSnapshot() ?? EmptyData,
            };

            using (var frame = compositor.ComposeFrame(Volatile.Read(ref _placements), context, Volatile.Read(ref _backgroundImage)))
            {
                var pixels = FrameBufferExtractor.ToRgb565Bytes(frame, swapBytes: true);

                // A full frame is ~300 KB over a 12 Mbit/s link, and a Pi
                // shares one USB controller between every screen and the
                // network chip. Don't spend that on a picture the panel is
                // already showing.
                var unchanged = lastSent is not null
                    && pixels.AsSpan().SequenceEqual(lastSent)
                    && Stopwatch.GetElapsedTime(lastSentAt) < _keepAliveInterval;

                if (!unchanged)
                {
                    await BlitFrameAsync(parameters.Width, parameters.Height, pixels, cancellationToken);
                    lastSent = pixels;
                    lastSentAt = Stopwatch.GetTimestamp();
                }
            }

            try
            {
                await Task.Delay(_interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        LogLoopStopped(_transport.DeviceId);
    }

    /// <summary>
    /// One transfer at a time across every loop sharing the gate. Without it,
    /// loops that started together stay in step, so all the screens push a
    /// full frame in the same instant and starve each other of bus time.
    /// </summary>
    private async Task BlitFrameAsync(int width, int height, byte[] pixels, CancellationToken cancellationToken)
    {
        if (_transferGate is null)
        {
            await _transport.BlitAsync(0, 0, (ushort)width, (ushort)height, pixels, cancellationToken);
            return;
        }

        await _transferGate.WaitAsync(cancellationToken);
        try
        {
            await _transport.BlitAsync(0, 0, (ushort)width, (ushort)height, pixels, cancellationToken);
        }
        finally
        {
            _transferGate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting display loop for {DeviceId} at {Width}x{Height}.")]
    private partial void LogLoopStarted(string deviceId, ushort width, ushort height);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped display loop for {DeviceId}.")]
    private partial void LogLoopStopped(string deviceId);
}
