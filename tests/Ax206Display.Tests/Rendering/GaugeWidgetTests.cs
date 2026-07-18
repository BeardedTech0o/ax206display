using Ax206Display.Rendering.Widgets;
using SkiaSharp;

namespace Ax206Display.Tests.Rendering;

public class GaugeWidgetTests
{
    [Fact]
    public void Render_WithValuePresent_PaintsSomething()
    {
        var widget = new GaugeWidget("gauge", 80, 80, dataKey: "system.cpu.load", label: "CPU", unit: "%");
        Assert.True(RenderAndCheckForNonBlackPixel(widget, new Dictionary<string, object> { ["system.cpu.load"] = 42.5 }));
    }

    [Fact]
    public void Render_WithMissingValue_StillPaintsTheTrackAndPlaceholderInsteadOfThrowing()
    {
        var widget = new GaugeWidget("gauge", 80, 80, dataKey: "system.cpu.load");
        Assert.True(RenderAndCheckForNonBlackPixel(widget, new Dictionary<string, object>()));
    }

    [Fact]
    public void Render_ValueAboveMax_ClampsInsteadOfOverdrawingOrThrowing()
    {
        var widget = new GaugeWidget("gauge", 80, 80, dataKey: "k", minValue: 0, maxValue: 100);
        Assert.True(RenderAndCheckForNonBlackPixel(widget, new Dictionary<string, object> { ["k"] = 999.0 }));
    }

    [Fact]
    public void Render_InvertedRange_FallsBackInsteadOfThrowing()
    {
        // maxValue <= minValue would divide by a non-positive span - the
        // widget should tolerate this instead of producing NaN/Infinity.
        var widget = new GaugeWidget("gauge", 80, 80, dataKey: "k", minValue: 100, maxValue: 0);
        Assert.True(RenderAndCheckForNonBlackPixel(widget, new Dictionary<string, object> { ["k"] = 50.0 }));
    }

    [Fact]
    public void Render_VerySmallBox_DoesNotThrow()
    {
        var widget = new GaugeWidget("gauge", 4, 4, dataKey: "k");
        using var bitmap = new SKBitmap(4, 4, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);

        var context = new WidgetRenderContext { Now = DateTimeOffset.UtcNow, Data = new Dictionary<string, object> { ["k"] = 10.0 } };
        widget.Render(canvas, context);
    }

    [Fact]
    public void Render_WithExplicitLargeValueFontSize_ClampsInsteadOfOverflowingOrThrowing()
    {
        // A value size bigger than the whole widget must still be clamped
        // to fit inside the ring (GaugeWidget.DrawValue) rather than honored
        // literally the way a plain rectangular widget would.
        var widget = new GaugeWidget("gauge", 80, 80, dataKey: "k", valueFontSizePx: 500f);
        Assert.True(RenderAndCheckForNonBlackPixel(widget, new Dictionary<string, object> { ["k"] = 42.0 }));
    }

    [Fact]
    public void Render_WithLongLabel_DoesNotThrow()
    {
        var widget = new GaugeWidget("gauge", 80, 80, dataKey: "k", label: "A Really Long Label That Should Shrink To Fit");
        Assert.True(RenderAndCheckForNonBlackPixel(widget, new Dictionary<string, object> { ["k"] = 42.0 }));
    }

    [Fact]
    public void Render_WithLabel_PaintsSomethingInTheFooterBelowTheRing()
    {
        var widget = new GaugeWidget("gauge", 100, 100, dataKey: "k", label: "Blocked");

        using var bitmap = new SKBitmap(widget.Width, widget.Height, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var context = new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero),
            Data = new Dictionary<string, object> { ["k"] = 42.0 },
        };
        widget.Render(canvas, context);
        canvas.Flush();

        // The footer strip is the bottom ~26% of the widget (see
        // GaugeWidget.FooterHeightFraction) and is entirely outside the
        // ring's bounding square - something should be painted there (the
        // label), confirming it isn't confined to the circle above it.
        var footerTop = (int)(widget.Height * 0.8);
        var footerHasPixels = false;
        for (var y = footerTop; y < widget.Height; y++)
        {
            for (var x = 0; x < widget.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.Black)
                {
                    footerHasPixels = true;
                }
            }
        }

        Assert.True(footerHasPixels);
    }

    private static bool RenderAndCheckForNonBlackPixel(GaugeWidget widget, Dictionary<string, object> data)
    {
        using var bitmap = new SKBitmap(widget.Width, widget.Height, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var context = new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero),
            Data = data,
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
