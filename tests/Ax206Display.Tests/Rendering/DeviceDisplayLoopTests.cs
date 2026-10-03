using Ax206Display.Protocol.Commands;
using Ax206Display.Rendering.Playback;
using Ax206Display.Rendering.Widgets;
using Ax206Display.Transport;
using Ax206Display.Transport.Mock;
using SkiaSharp;

namespace Ax206Display.Tests.Rendering;

public class DeviceDisplayLoopTests
{
    [Fact]
    public async Task RunAsync_QueriesRealResolutionAndBlitsFullFrames()
    {
        using var transport = new MockAx206Transport("mock-1", 10, 8);
        var loop = new DeviceDisplayLoop(transport, placements: [], TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource();
        var runTask = loop.RunAsync(cts.Token);

        await Task.Delay(100);
        cts.Cancel();
        await runTask;

        Assert.NotEmpty(transport.BlitCalls);

        var call = transport.BlitCalls[0];
        Assert.Equal(0, call.Left);
        Assert.Equal(0, call.Top);
        Assert.Equal(10, call.Right);
        Assert.Equal(8, call.Bottom);
        Assert.Equal(10 * 8 * 2, call.Pixels.Length);
    }

    [Fact]
    public async Task RunAsync_StopsBlittingShortlyAfterCancellation()
    {
        using var transport = new MockAx206Transport("mock-1", 4, 4);
        var loop = new DeviceDisplayLoop(transport, placements: [], TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource();
        var runTask = loop.RunAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await runTask;

        var countAtCancel = transport.BlitCalls.Count;
        await Task.Delay(50);

        Assert.Equal(countAtCancel, transport.BlitCalls.Count);
    }

    [Fact]
    public async Task RunAsync_RendersConfiguredWidgets()
    {
        using var transport = new MockAx206Transport("mock-1", 20, 20);
        var clock = new ClockWidget("clock-1", 20, 20);
        var placements = new[] { new WidgetPlacement(clock, 0, 0, ZOrder: 0) };
        var loop = new DeviceDisplayLoop(transport, placements, TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource();
        var runTask = loop.RunAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await runTask;

        Assert.NotEmpty(transport.BlitCalls);
    }

    [Fact]
    public async Task RunAsync_PassesProviderSnapshotToWidgets()
    {
        using var transport = new MockAx206Transport("mock-1", 20, 20);
        var hub = new RenderDataHub();
        hub.Publish("system.cpu.load", 55.0);

        var capture = new DataCapturingWidget("capture", 20, 20);
        var placements = new[] { new WidgetPlacement(capture, 0, 0, ZOrder: 0) };
        var loop = new DeviceDisplayLoop(transport, placements, TimeSpan.FromMilliseconds(10), hub);

        using var cts = new CancellationTokenSource();
        var runTask = loop.RunAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await runTask;

        Assert.NotNull(capture.LastData);
        Assert.Equal(55.0, capture.LastData!["system.cpu.load"]);
    }

    [Fact]
    public async Task UpdatePlacements_TakesEffectWithoutRestartingTheLoop()
    {
        using var transport = new MockAx206Transport("mock-1", 20, 20);
        var loop = new DeviceDisplayLoop(transport, placements: [], TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource();
        var runTask = loop.RunAsync(cts.Token);

        await Task.Delay(50);
        var countBeforeUpdate = transport.BlitCalls.Count;

        var clock = new ClockWidget("clock-1", 20, 20);
        loop.UpdatePlacements([new WidgetPlacement(clock, 0, 0, ZOrder: 0)]);

        await Task.Delay(50);
        cts.Cancel();
        await runTask;

        Assert.True(transport.BlitCalls.Count > countBeforeUpdate, "Expected more frames to be blitted after the update.");

        var lastFramePixels = transport.BlitCalls[^1].Pixels;
        Assert.Contains(lastFramePixels, b => b != 0);
    }

    [Fact]
    public async Task UpdateBackgroundImage_TakesEffectWithoutRestartingTheLoop()
    {
        using var transport = new MockAx206Transport("mock-1", 20, 20);
        var loop = new DeviceDisplayLoop(transport, placements: [], TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource();
        var runTask = loop.RunAsync(cts.Token);

        await Task.Delay(50);

        using var background = new SkiaSharp.SKBitmap(4, 4, SkiaSharp.SKColorType.Rgb565, SkiaSharp.SKAlphaType.Opaque);
        background.Erase(SkiaSharp.SKColors.White);
        loop.UpdateBackgroundImage(background);

        await Task.Delay(50);
        cts.Cancel();
        await runTask;

        var lastFramePixels = transport.BlitCalls[^1].Pixels;
        Assert.Contains(lastFramePixels, b => b != 0);
    }

    [Fact]
    public async Task SetBrightnessAsync_ForwardsThePropertyWriteToTheTransport()
    {
        using var transport = new MockAx206Transport("mock-1", 10, 8);
        var loop = new DeviceDisplayLoop(transport, placements: [], TimeSpan.FromMilliseconds(10));

        await loop.SetBrightnessAsync(5);

        Assert.Equal((ushort)5, transport.Properties[Ax206Property.Brightness]);
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(99, 7)]
    public async Task SetBrightnessAsync_ClampsOutOfRangeValuesToTheValid0To7Range(int requested, int expected)
    {
        using var transport = new MockAx206Transport("mock-1", 10, 8);
        var loop = new DeviceDisplayLoop(transport, placements: [], TimeSpan.FromMilliseconds(10));

        await loop.SetBrightnessAsync(requested);

        Assert.Equal((ushort)expected, transport.Properties[Ax206Property.Brightness]);
    }

    [Fact]
    public async Task RunAsync_UnchangedFrame_IsSentOnceNotEveryTick()
    {
        using var transport = new MockAx206Transport("mock-1", 10, 8);
        var loop = new DeviceDisplayLoop(transport, placements: [], TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource();
        var runTask = loop.RunAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        await runTask;

        Assert.Single(transport.BlitCalls);
    }

    [Fact]
    public async Task RunAsync_UnchangedFrame_IsSentAgainOnceTheKeepAliveHasElapsed()
    {
        using var transport = new MockAx206Transport("mock-1", 10, 8);
        var loop = new DeviceDisplayLoop(
            transport,
            placements: [],
            TimeSpan.FromMilliseconds(10),
            keepAliveInterval: TimeSpan.FromMilliseconds(60));

        using var cts = new CancellationTokenSource();
        var runTask = loop.RunAsync(cts.Token);
        await Task.Delay(400);
        cts.Cancel();
        await runTask;

        Assert.InRange(transport.BlitCalls.Count, 2, 12);
    }

    [Fact]
    public async Task RunAsync_ChangingFrame_IsSentEveryTick()
    {
        using var transport = new MockAx206Transport("mock-1", 10, 8);
        var flipping = new FlippingWidget("flip", 10, 8);
        var loop = new DeviceDisplayLoop(transport, [new WidgetPlacement(flipping, 0, 0, ZOrder: 0)], TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource();
        var runTask = loop.RunAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        await runTask;

        Assert.True(transport.BlitCalls.Count >= 5, $"Expected a changing frame to be sent each tick, saw {transport.BlitCalls.Count}.");
    }

    [Fact]
    public async Task RunAsync_LoopsSharingATransferGate_NeverTransferAtTheSameTime()
    {
        using var first = new OverlapTrackingTransport("one", 10, 8);
        using var second = new OverlapTrackingTransport("two", 10, 8);
        var gate = new SemaphoreSlim(1, 1);

        var firstLoop = new DeviceDisplayLoop(first, [new WidgetPlacement(new FlippingWidget("a", 10, 8), 0, 0, ZOrder: 0)], TimeSpan.FromMilliseconds(5), transferGate: gate);
        var secondLoop = new DeviceDisplayLoop(second, [new WidgetPlacement(new FlippingWidget("b", 10, 8), 0, 0, ZOrder: 0)], TimeSpan.FromMilliseconds(5), transferGate: gate);

        using var cts = new CancellationTokenSource();
        var tasks = new[] { RunUntilCancelledAsync(firstLoop, cts.Token), RunUntilCancelledAsync(secondLoop, cts.Token) };
        await Task.Delay(400);
        cts.Cancel();
        await Task.WhenAll(tasks);

        Assert.True(first.Transfers > 2 && second.Transfers > 2, "Both screens should still get their frames.");
        Assert.Equal(1, OverlapTrackingTransport.MaxConcurrent(first, second));
    }

    /// <summary>
    /// A loop cancelled while it waits its turn at the gate (or mid-transfer)
    /// throws OperationCanceledException, which the display manager already
    /// treats as an ordinary shutdown - so the test does too.
    /// </summary>
    private static async Task RunUntilCancelledAsync(DeviceDisplayLoop loop, CancellationToken cancellationToken)
    {
        try
        {
            await loop.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Draws a different color every render, so every frame differs from the last.</summary>
    private sealed class FlippingWidget : IWidget
    {
        private int _renders;

        public FlippingWidget(string id, int width, int height)
        {
            Id = id;
            Width = width;
            Height = height;
        }

        public string Id { get; }

        public int Width { get; }

        public int Height { get; }

        public void Render(SKCanvas canvas, WidgetRenderContext context)
        {
            canvas.Clear(Interlocked.Increment(ref _renders) % 2 == 0 ? SKColors.White : SKColors.Red);
        }
    }

    /// <summary>A transport whose blits take a while, recording how many were in flight at once across every instance.</summary>
    private sealed class OverlapTrackingTransport : IAx206Transport
    {
        private static int _inFlight;
        private static int _maxInFlight;
        private readonly ushort _width;
        private readonly ushort _height;
        private int _transfers;

        public OverlapTrackingTransport(string deviceId, ushort width, ushort height)
        {
            DeviceId = deviceId;
            _width = width;
            _height = height;
            Interlocked.Exchange(ref _inFlight, 0);
            Interlocked.Exchange(ref _maxInFlight, 0);
        }

        public string DeviceId { get; }

        public int Transfers => _transfers;

        public static int MaxConcurrent(params OverlapTrackingTransport[] transports) => Volatile.Read(ref _maxInFlight);

        public Task<LcdParametersResponse> GetLcdParametersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LcdParametersResponse(_width, _height, IsMarkerValid: true));

        public Task SetPropertyAsync(Ax206Property propertyToken, ushort value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task BlitAsync(ushort left, ushort top, ushort right, ushort bottom, ReadOnlyMemory<byte> rgb565BigEndianPixels, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _inFlight);
            int seen;
            while ((seen = Volatile.Read(ref _maxInFlight)) < now && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen)
            {
            }

            try
            {
                await Task.Delay(15, CancellationToken.None);
                Interlocked.Increment(ref _transfers);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class DataCapturingWidget : IWidget
    {
        public DataCapturingWidget(string id, int width, int height)
        {
            Id = id;
            Width = width;
            Height = height;
        }

        public string Id { get; }

        public int Width { get; }

        public int Height { get; }

        public IReadOnlyDictionary<string, object>? LastData { get; private set; }

        public void Render(SkiaSharp.SKCanvas canvas, WidgetRenderContext context)
        {
            LastData = context.Data;
        }
    }
}
