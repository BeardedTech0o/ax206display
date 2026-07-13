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

    internal static void DrawCentered(SKCanvas canvas, string text, int width, int height, SKColor color, WidgetFontStyle? fontStyle = null)
    {
        var style = fontStyle ?? WidgetFontStyle.Default;
        var typeface = ResolveTypeface(style);

        using var font = new SKFont(typeface, height * HeightFraction * style.SizeScale);
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

    private static SKTypeface ResolveTypeface(WidgetFontStyle style)
    {
        if (string.IsNullOrEmpty(style.FontFamily) && !style.Bold && !style.Italic)
        {
            return SKTypeface.Default;
        }

        var skStyle = new SKFontStyle(
            style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        // Deliberately not disposed: a typeface resolved by family name/style
        // can alias a shared or cached instance (observed aliasing
        // SKTypeface.Default itself on this environment's limited font set) -
        // disposing it risks invalidating that shared instance for every
        // other widget. The set of distinct (family, bold, italic)
        // combinations actually in use is small and bounded, so leaking them
        // for the process's lifetime is the safe tradeoff.
        return SKTypeface.FromFamilyName(style.FontFamily, skStyle);
    }
}
