using Ax206Display.Config.Models;
using Ax206Display.Rendering.Widgets;
using SkiaSharp;

namespace Ax206Display.Tests.Rendering;

/// <summary>
/// Functional coverage for fixed pixel font sizes ("fontSizePx"): the size
/// must actually change what gets painted, not just survive construction.
/// </summary>
public class WidgetFontSizeTests
{
    [Fact]
    public void FixedSizePixels_LargerSizePaintsMorePixels()
    {
        var smallPainted = CountPaintedPixels(fontSizePx: 8);
        var largePainted = CountPaintedPixels(fontSizePx: 48);

        Assert.True(smallPainted > 0, "Expected 8px text to paint something.");
        Assert.True(
            largePainted > smallPainted * 2,
            $"Expected 48px text to paint far more pixels than 8px text, got {largePainted} vs {smallPainted}.");
    }

    [Fact]
    public void FixedSizePixels_IsNotShrunkToFitTheBoxWidth()
    {
        // Auto sizing shrinks text to the box width, so a narrow box paints
        // fewer pixels. A fixed pixel size must ignore the box width (the
        // compositor clips overflow instead) - the same 30px text in a
        // narrow box paints just as tall as in a wide one.
        var wideTallest = TallestPaintedRun(boxWidth: 400, fontSizePx: 30);
        var narrowTallest = TallestPaintedRun(boxWidth: 60, fontSizePx: 30);

        Assert.True(wideTallest > 0 && narrowTallest > 0, "Expected text in both boxes.");
        Assert.True(
            narrowTallest >= wideTallest * 0.9,
            $"Expected fixed-size text to stay the same height in a narrow box, got {narrowTallest}px vs {wideTallest}px.");
    }

    private static int CountPaintedPixels(int fontSizePx)
    {
        using var bitmap = RenderText("Hello", boxWidth: 400, boxHeight: 120, fontSizePx);

        var painted = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.Black)
                {
                    painted++;
                }
            }
        }

        return painted;
    }

    /// <summary>The tallest vertical run of painted pixels in any column - a proxy for the rendered glyph height.</summary>
    private static int TallestPaintedRun(int boxWidth, int fontSizePx)
    {
        using var bitmap = RenderText("Hello", boxWidth, boxHeight: 120, fontSizePx);

        var tallest = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            var top = int.MaxValue;
            var bottom = int.MinValue;
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.Black)
                {
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }

            if (bottom >= top)
            {
                tallest = Math.Max(tallest, bottom - top + 1);
            }
        }

        return tallest;
    }

    private static SKBitmap RenderText(string text, int boxWidth, int boxHeight, int fontSizePx)
    {
        var config = new WidgetConfig
        {
            Id = "text-1",
            Type = "text",
            X = 0,
            Y = 0,
            Width = boxWidth,
            Height = boxHeight,
        };
        config.Settings["text"] = text;
        config.Settings["fontSizePx"] = fontSizePx;

        var widget = WidgetFactory.Create(config);

        var bitmap = new SKBitmap(boxWidth, boxHeight, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        widget.Render(canvas, new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
            Data = new Dictionary<string, object>(),
        });
        canvas.Flush();

        return bitmap;
    }
}
