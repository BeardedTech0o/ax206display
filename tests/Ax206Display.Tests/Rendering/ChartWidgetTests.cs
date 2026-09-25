using Ax206Display.Rendering.Playback;
using Ax206Display.Rendering.Widgets;
using SkiaSharp;

namespace Ax206Display.Tests.Rendering;

public class ChartWidgetTests
{
    [Fact]
    public void Render_WithHistory_PaintsSomething()
    {
        var hub = new RenderDataHub();
        hub.Publish("net.download", 10.0);
        hub.Publish("net.download", 20.0);
        hub.Publish("net.download", 15.0);

        var widget = new ChartWidget("chart", 100, 60, dataKey: "net.download", label: "Down", unit: " Mbps");
        Assert.True(RenderAndCheckForNonBlackPixel(widget, hub));
    }

    [Fact]
    public void Render_WithNoHistory_PaintsPlaceholderInsteadOfThrowing()
    {
        var hub = new RenderDataHub();
        var widget = new ChartWidget("chart", 100, 60, dataKey: "never-published");

        Assert.True(RenderAndCheckForNonBlackPixel(widget, hub));
    }

    [Fact]
    public void Render_WithSingleSample_DoesNotThrow()
    {
        var hub = new RenderDataHub();
        hub.Publish("k", 42.0);

        var widget = new ChartWidget("chart", 100, 60, dataKey: "k");
        RenderAndCheckForNonBlackPixel(widget, hub);
    }

    [Fact]
    public void Render_WithFlatHistory_DoesNotThrow()
    {
        // Every sample the same value - a naive (max - min) range would
        // divide by zero when scaling y-coordinates.
        var hub = new RenderDataHub();
        hub.Publish("k", 5.0);
        hub.Publish("k", 5.0);
        hub.Publish("k", 5.0);

        var widget = new ChartWidget("chart", 100, 60, dataKey: "k");
        Assert.True(RenderAndCheckForNonBlackPixel(widget, hub));
    }

    [Fact]
    public void Render_WithNoDataProviderAtAll_PaintsPlaceholderInsteadOfThrowing()
    {
        var widget = new ChartWidget("chart", 100, 60, dataKey: "k");

        using var bitmap = new SKBitmap(widget.Width, widget.Height, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var context = new WidgetRenderContext { Now = DateTimeOffset.UtcNow, Data = new Dictionary<string, object>() };
        widget.Render(canvas, context);
    }

    [Fact]
    public void Render_VerySmallBox_DoesNotThrow()
    {
        var hub = new RenderDataHub();
        hub.Publish("k", 1.0);
        hub.Publish("k", 2.0);

        var widget = new ChartWidget("chart", 4, 4, dataKey: "k");
        using var bitmap = new SKBitmap(4, 4, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);

        var context = new WidgetRenderContext { Now = DateTimeOffset.UtcNow, Data = new Dictionary<string, object>(), DataProvider = hub };
        widget.Render(canvas, context);
    }

    private static bool RenderAndCheckForNonBlackPixel(ChartWidget widget, RenderDataHub hub)
    {
        using var bitmap = new SKBitmap(widget.Width, widget.Height, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var context = new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero),
            Data = new Dictionary<string, object>(),
            DataProvider = hub,
        };

        widget.Render(canvas, context);
        canvas.Flush();

        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.Black)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
