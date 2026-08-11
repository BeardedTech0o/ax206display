using Ax206Display.Rendering.Compositing;
using Ax206Display.Rendering.Widgets;
using SkiaSharp;

namespace Ax206Display.Tests.Rendering;

public class FrameCompositorTests
{
    [Fact]
    public void ComposeFrame_ProducesCanvasSizedRgb565Bitmap()
    {
        var compositor = new FrameCompositor(480, 320);
        var context = EmptyContext();

        using var frame = compositor.ComposeFrame([], context);

        Assert.Equal(480, frame.Width);
        Assert.Equal(320, frame.Height);
        Assert.Equal(SKColorType.Rgb565, frame.ColorType);
    }

    [Fact]
    public void ComposeFrame_HigherZOrderDrawsOnTop()
    {
        var compositor = new FrameCompositor(10, 10);
        var bottom = new SolidColorWidget("bottom", 10, 10, SKColors.Red);
        var top = new SolidColorWidget("top", 10, 10, SKColors.Blue);
        var context = EmptyContext();

        using var frame = compositor.ComposeFrame(
            [new WidgetPlacement(bottom, 0, 0, ZOrder: 0), new WidgetPlacement(top, 0, 0, ZOrder: 1)],
            context);

        var pixel = frame.GetPixel(5, 5);
        Assert.Equal(new SKColor(pixel.Red, pixel.Green, pixel.Blue), Quantize(SKColors.Blue));
    }

    [Fact]
    public void ComposeFrame_WidgetIsClippedAndTranslatedToItsPlacement()
    {
        var compositor = new FrameCompositor(20, 20, SKColors.Black);
        var widget = new SolidColorWidget("fill", 5, 5, SKColors.White);
        var context = EmptyContext();

        using var frame = compositor.ComposeFrame([new WidgetPlacement(widget, 10, 10, ZOrder: 0)], context);

        // Inside the widget's placement rectangle: painted white.
        AssertApproximatelyWhite(frame.GetPixel(12, 12));
        // Outside it: still the untouched background.
        Assert.Equal(SKColors.Black, frame.GetPixel(1, 1));
    }

    [Fact]
    public void ComposeFrame_WithBackgroundImage_DrawsItBeneathWidgets()
    {
        var compositor = new FrameCompositor(20, 20, SKColors.Black);
        using var background = new SKBitmap(4, 4, SKColorType.Rgb565, SKAlphaType.Opaque);
        background.Erase(SKColors.Green);

        var widget = new SolidColorWidget("fill", 5, 5, SKColors.White);
        var context = EmptyContext();

        using var frame = compositor.ComposeFrame([new WidgetPlacement(widget, 10, 10, ZOrder: 0)], context, background);

        // Stretched to fill the whole canvas, so any point outside the
        // widget's placement should show the background image's color
        // instead of the plain background color.
        var pixel = frame.GetPixel(1, 1);
        Assert.Equal(new SKColor(pixel.Red, pixel.Green, pixel.Blue), Quantize(SKColors.Green));

        // Still drawn beneath the widget.
        AssertApproximatelyWhite(frame.GetPixel(12, 12));
    }

    private static WidgetRenderContext EmptyContext() => new()
    {
        Now = DateTimeOffset.UtcNow,
        Data = new Dictionary<string, object>(),
    };

    private static SKColor Quantize(SKColor color)
    {
        // Round-trip through Rgb565 so exact-color comparisons account for
        // the format's reduced bit depth (5/6/5 bits per channel).
        using var bitmap = new SKBitmap(1, 1, SKColorType.Rgb565, SKAlphaType.Opaque);
        bitmap.SetPixel(0, 0, color);
        var pixel = bitmap.GetPixel(0, 0);
        return new SKColor(pixel.Red, pixel.Green, pixel.Blue);
    }

    private static void AssertApproximatelyWhite(SKColor color)
    {
        Assert.True(color.Red > 240 && color.Green > 240 && color.Blue > 240, $"Expected near-white, got {color}.");
    }

    private sealed class SolidColorWidget(string id, int width, int height, SKColor color) : IWidget
    {
        public string Id { get; } = id;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public void Render(SKCanvas canvas, WidgetRenderContext context)
        {
            canvas.Clear(color);
        }
    }
}
