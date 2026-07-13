using Ax206Display.Rendering.Widgets;
using SkiaSharp;

namespace Ax206Display.Tests.Rendering;

public class ClockWidgetTests
{
    [Fact]
    public void Render_DrawsWithoutThrowingAndTouchesTheCanvas()
    {
        var widget = new ClockWidget("clock", 200, 60);
        using var bitmap = new SKBitmap(200, 60, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var context = new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 3, 12, 34, 56, TimeSpan.Zero),
            Data = new Dictionary<string, object>(),
        };

        widget.Render(canvas, context);
        canvas.Flush();

        var hasNonBlackPixel = false;
        for (var x = 0; x < bitmap.Width && !hasNonBlackPixel; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.Black)
                {
                    hasNonBlackPixel = true;
                    break;
                }
            }
        }

        Assert.True(hasNonBlackPixel, "Expected the clock text to paint at least one non-background pixel.");
    }

    [Fact]
    public void Render_LongFormatInNarrowWidget_ShrinksTextToFit()
    {
        // 100px wide is far too narrow for "HH:mm:ss" at the height-derived
        // font size; without width-fitting the centered text bleeds off both
        // edges of the widget.
        var widget = new ClockWidget("clock", 100, 60);
        using var bitmap = new SKBitmap(100, 60, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var context = new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 3, 23, 18, 45, TimeSpan.Zero),
            Data = new Dictionary<string, object>(),
        };

        widget.Render(canvas, context);
        canvas.Flush();

        var anyTextPixel = false;
        var edgeColumnsTouched = false;
        for (var y = 0; y < bitmap.Height; y++)
        {
            if (bitmap.GetPixel(0, y) != SKColors.Black || bitmap.GetPixel(bitmap.Width - 1, y) != SKColors.Black)
            {
                edgeColumnsTouched = true;
            }
        }

        for (var x = 0; x < bitmap.Width && !anyTextPixel; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.Black)
                {
                    anyTextPixel = true;
                    break;
                }
            }
        }

        Assert.True(anyTextPixel, "Expected the clock text to paint at least one non-background pixel.");
        Assert.False(edgeColumnsTouched, "Expected the shrunk-to-fit clock text to stay clear of the widget's left/right edges.");
    }

    [Fact]
    public void Render_WithUnknownFontFamily_FallsBackInsteadOfThrowing()
    {
        // SkiaSharp's font-family lookup substitutes a fallback typeface for
        // a name it doesn't recognize rather than throwing or returning
        // null - a saved config referencing a font that isn't installed on
        // this machine must still render, not crash the display loop.
        var widget = new ClockWidget("clock", 200, 60, fontStyle: new WidgetFontStyle(FontFamily: "Definitely Not A Real Font 12345"));
        using var bitmap = new SKBitmap(200, 60, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var context = new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 3, 12, 34, 56, TimeSpan.Zero),
            Data = new Dictionary<string, object>(),
        };

        var exception = Record.Exception(() => widget.Render(canvas, context));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Render_WithBoldOrItalic_DoesNotThrow(bool bold, bool italic)
    {
        var widget = new ClockWidget("clock", 200, 60, fontStyle: new WidgetFontStyle(Bold: bold, Italic: italic));
        using var bitmap = new SKBitmap(200, 60, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var context = new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 3, 12, 34, 56, TimeSpan.Zero),
            Data = new Dictionary<string, object>(),
        };

        var exception = Record.Exception(() => widget.Render(canvas, context));

        Assert.Null(exception);
    }

    [Fact]
    public void Render_WithFontScale_StillFitsWithinBounds()
    {
        // A large scale (e.g. "Extra Large") pushes the height-derived size
        // well past the widget's width for a wide time format - the
        // shrink-to-fit clamp must still apply on top of the scale.
        var widget = new ClockWidget("clock", 100, 60, fontStyle: new WidgetFontStyle(SizeScale: 1.5f));
        using var bitmap = new SKBitmap(100, 60, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var context = new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 3, 23, 18, 45, TimeSpan.Zero),
            Data = new Dictionary<string, object>(),
        };

        widget.Render(canvas, context);
        canvas.Flush();

        var edgeColumnsTouched = false;
        for (var y = 0; y < bitmap.Height; y++)
        {
            if (bitmap.GetPixel(0, y) != SKColors.Black || bitmap.GetPixel(bitmap.Width - 1, y) != SKColors.Black)
            {
                edgeColumnsTouched = true;
            }
        }

        Assert.False(edgeColumnsTouched, "Expected the shrunk-to-fit text to stay clear of the widget's left/right edges even at a large font scale.");
    }
}
