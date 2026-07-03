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
}
