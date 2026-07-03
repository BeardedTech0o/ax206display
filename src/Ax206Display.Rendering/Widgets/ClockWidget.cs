using System.Globalization;
using SkiaSharp;

namespace Ax206Display.Rendering.Widgets;

public sealed class ClockWidget : IWidget
{
    private readonly SKColor _textColor;
    private readonly string _timeFormat;

    public ClockWidget(string id, int width, int height, string timeFormat = "HH:mm:ss", SKColor? textColor = null)
    {
        Id = id;
        Width = width;
        Height = height;
        _timeFormat = timeFormat;
        _textColor = textColor ?? SKColors.White;
    }

    public string Id { get; }

    public int Width { get; }

    public int Height { get; }

    public void Render(SKCanvas canvas, WidgetRenderContext context)
    {
        using var font = new SKFont(SKTypeface.Default, Height * 0.6f);
        using var paint = new SKPaint { Color = _textColor, IsAntialias = true };

        // Invariant culture so the display always renders Latin digits
        // regardless of the host machine's locale - a small embedded LCD
        // font may not have glyphs for other numbering systems.
        var text = context.Now.ToString(_timeFormat, CultureInfo.InvariantCulture);
        var textWidth = font.MeasureText(text, paint);

        var x = (Width - textWidth) / 2f;
        var y = Height / 2f - (font.Metrics.Ascent + font.Metrics.Descent) / 2f;

        canvas.DrawText(text, x, y, font, paint);
    }
}
