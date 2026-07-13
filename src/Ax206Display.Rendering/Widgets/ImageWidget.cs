using SkiaSharp;

namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// Draws a single still frame. Animated GIF playback (frame scheduling/looping)
/// is layered on top by whatever owns the render loop, which calls
/// <see cref="SetFrame"/> before each <see cref="Render"/> as needed.
/// </summary>
public sealed class ImageWidget : IWidget, IDisposable
{
    private SKBitmap? _frame;

    public ImageWidget(string id, int width, int height)
    {
        Id = id;
        Width = width;
        Height = height;
    }

    public string Id { get; }

    public int Width { get; }

    public int Height { get; }

    public void SetFrame(SKBitmap frame)
    {
        _frame = frame;
    }

    public void Render(SKCanvas canvas, WidgetRenderContext context)
    {
        if (_frame is null)
        {
            return;
        }

        var destRect = new SKRect(0, 0, Width, Height);
        canvas.DrawBitmap(_frame, destRect);
    }

    public void Dispose()
    {
        _frame?.Dispose();
    }
}
