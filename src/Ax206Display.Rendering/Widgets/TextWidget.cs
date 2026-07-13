using SkiaSharp;

namespace Ax206Display.Rendering.Widgets;

/// <summary>A static label - section titles, device names, decorations.</summary>
public sealed class TextWidget : IWidget
{
    private readonly string _text;
    private readonly SKColor _textColor;

    public TextWidget(string id, int width, int height, string text, SKColor? textColor = null)
    {
        Id = id;
        Width = width;
        Height = height;
        _text = text;
        _textColor = textColor ?? SKColors.White;
    }

    public string Id { get; }

    public int Width { get; }

    public int Height { get; }

    public void Render(SKCanvas canvas, WidgetRenderContext context)
    {
        WidgetTextRenderer.DrawCentered(canvas, _text, Width, Height, _textColor);
    }
}
