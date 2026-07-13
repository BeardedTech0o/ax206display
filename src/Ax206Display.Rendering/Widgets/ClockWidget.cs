using System.Globalization;
using SkiaSharp;

namespace Ax206Display.Rendering.Widgets;

public sealed class ClockWidget : IWidget
{
    private readonly SKColor _textColor;
    private readonly string _timeFormat;
    private readonly WidgetFontStyle _fontStyle;

    public ClockWidget(string id, int width, int height, string timeFormat = "HH:mm:ss", SKColor? textColor = null, WidgetFontStyle? fontStyle = null)
    {
        Id = id;
        Width = width;
        Height = height;
        _timeFormat = timeFormat;
        _textColor = textColor ?? SKColors.White;
        _fontStyle = fontStyle ?? WidgetFontStyle.Default;
    }

    public string Id { get; }

    public int Width { get; }

    public int Height { get; }

    public void Render(SKCanvas canvas, WidgetRenderContext context)
    {
        // Invariant culture so the display always renders Latin digits
        // regardless of the host machine's locale - a small embedded LCD
        // font may not have glyphs for other numbering systems.
        var text = context.Now.ToString(_timeFormat, CultureInfo.InvariantCulture);
        WidgetTextRenderer.DrawCentered(canvas, text, Width, Height, _textColor, _fontStyle);
    }
}
