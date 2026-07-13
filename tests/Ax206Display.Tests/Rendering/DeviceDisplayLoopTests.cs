using Ax206Display.Rendering.Playback;
using Ax206Display.Rendering.Widgets;
using Ax206Display.Transport.Mock;

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
