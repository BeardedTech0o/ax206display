using SkiaSharp;

namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// A single drawable element of a device's layout. Implementations must be
/// side-effect free with respect to their bounds - <see cref="Render"/> draws
/// only within the canvas area the compositor has already clipped/translated to.
/// </summary>
public interface IWidget
{
    string Id { get; }

    int Width { get; }

    int Height { get; }

    void Render(SKCanvas canvas, WidgetRenderContext context);
}
