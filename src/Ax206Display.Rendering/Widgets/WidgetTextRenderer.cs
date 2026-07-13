using SkiaSharp;

namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// Shared text drawing for widgets: horizontally and vertically centered,
/// sized from the widget height but shrunk to fit the width, so no time
/// format, label, or sensor value ever bleeds past the widget's bounds.
/// </summary>
internal static class WidgetTextRenderer
{
    private const float HeightFraction = 0.6f;
    private const float MaxWidthFraction = 0.95f;

    internal static void DrawCentered(SKCanvas canvas, string text, int width, int height, SKColor color)
    {
        using var font = new SKFont(SKTypeface.Default, height * HeightFraction);
        using var paint = new SKPaint { Color = color, IsAntialias = true };

        var textWidth = font.MeasureText(text, paint);

        var maxTextWidth = width * MaxWidthFraction;
        if (textWidth > maxTextWidth)
        {
            font.Size *= maxTextWidth / textWidth;
            textWidth = font.MeasureText(text, paint);
        }

        var x = (width - textWidth) / 2f;
        var y = height / 2f - (font.Metrics.Ascent + font.Metrics.Descent) / 2f;

        canvas.DrawText(text, x, y, font, paint);
    }
}
