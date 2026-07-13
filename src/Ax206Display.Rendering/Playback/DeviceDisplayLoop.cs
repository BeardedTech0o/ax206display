using Ax206Display.Rendering.Compositing;
using Ax206Display.Rendering.PixelFormats;
using Ax206Display.Rendering.Widgets;
using Ax206Display.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ax206Display.Rendering.Playback;

/// <summary>
/// Drives one physical display: queries its real resolution (never trusts a
/// stored config value - see docs/protocol-spec.md on not hardcoding display
/// properties), then composes and blits frames on a timer until cancelled.
/// </summary>
public sealed partial class DeviceDisplayLoop
{
    private static readonly IReadOnlyDictionary<string, object> EmptyData = new Dictionary<string, object>();

    private readonly IAx206Transport _transport;
    private readonly IReadOnlyList<WidgetPlacement> _placements;
    private readonly TimeSpan _interval;
    private readonly IRenderDataProvider? _dataProvider;
    private readonly ILogger<DeviceDisplayLoop> _logger;

    public DeviceDisplayLoop(
        IAx206Transport transport,
        IReadOnlyList<WidgetPlacement> placements,
        TimeSpan interval,
        IRenderDataProvider? dataProvider = null,
        ILogger<DeviceDisplayLoop>? logger = null)
    {
        _transport = transport;
        _placements = placements;
        _interval = interval;
        _dataProvider = dataProvider;
        _logger = logger ?? NullLogger<DeviceDisplayLoop>.Instance;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var parameters = await _transport.GetLcdParametersAsync(cancellationToken);
        LogLoopStarted(_transport.DeviceId, parameters.Width, parameters.Height);

        var compositor = new FrameCompositor(parameters.Width, parameters.Height);

        while (!cancellationToken.IsCancellationRequested)
        {
            var context = new WidgetRenderContext
            {
                Now = DateTimeOffset.Now,
                Data = _dataProvider?.GetSnapshot() ?? EmptyData,
            };

            using (var frame = compositor.ComposeFrame(_placements, context))
            {
                var pixels = FrameBufferExtractor.ToRgb565Bytes(frame, swapBytes: true);
                await _transport.BlitAsync(0, 0, parameters.Width, parameters.Height, pixels, cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting display loop for {DeviceId} at {Width}x{Height}.")]
    private partial void LogLoopStarted(string deviceId, ushort width, ushort height);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped display loop for {DeviceId}.")]
    private partial void LogLoopStopped(string deviceId);
}
