using Ax206Display.Rendering.Widgets;
using SkiaSharp;

namespace Ax206Display.Rendering.Compositing;

/// <summary>
/// Renders a device's widget layout into a single bitmap, one frame at a time.
/// Widgets are drawn in ascending <see cref="WidgetPlacement.ZOrder"/> order,
/// each clipped and translated to its own placement rectangle so a widget's
/// <see cref="IWidget.Render"/> can draw in local (0,0)-origin coordinates.
/// </summary>
public sealed class FrameCompositor
{
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;
    private readonly SKColor _backgroundColor;

    public FrameCompositor(int canvasWidth, int canvasHeight, SKColor? backgroundColor = null)
    {
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        _backgroundColor = backgroundColor ?? SKColors.Black;
    }

    public SKBitmap ComposeFrame(IReadOnlyList<WidgetPlacement> placements, WidgetRenderContext context)
    {
        var bitmap = new SKBitmap(_canvasWidth, _canvasHeight, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(_backgroundColor);

        foreach (var placement in placements.OrderBy(p => p.ZOrder))
        {
            var bounds = new SKRect(placement.X, placement.Y, placement.X + placement.Widget.Width, placement.Y + placement.Widget.Height);

            canvas.Save();
            canvas.ClipRect(bounds);
            canvas.Translate(bounds.Left, bounds.Top);
            placement.Widget.Render(canvas, context);
            canvas.Restore();
        }

        canvas.Flush();
        return bitmap;
    }
}
