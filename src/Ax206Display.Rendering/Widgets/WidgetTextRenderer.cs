using System.Reflection;
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

    // "Space Mono" (WidgetCatalog.FontFamilies) isn't a Windows system font,
    // so resolving it by family name the way every other listed font is
    // resolved would silently fall back to the default typeface on a
    // machine that doesn't happen to have it installed. Bundled as an
    // embedded resource (Fonts/SpaceMono/*.ttf, see the .csproj) instead, so
    // it renders identically everywhere. Cached per (bold, italic) rather
    // than re-decoded from the embedded bytes on every call - this runs
    // once per frame per Space Mono widget.
    private const string SpaceMonoFamilyName = "Space Mono";
    private static readonly Dictionary<(bool Bold, bool Italic), SKTypeface> SpaceMonoTypefaces = [];
    private static readonly object SpaceMonoLock = new();

    internal static void DrawCentered(SKCanvas canvas, string text, int width, int height, SKColor color, WidgetFontStyle? fontStyle = null, bool alwaysShrinkToFitWidth = false)
    {
        var style = fontStyle ?? WidgetFontStyle.Default;
        var typeface = ResolveTypeface(style);

        // A fixed pixel size is honored exactly (that's its whole point);
        // otherwise size from the box height with the legacy scale multiplier.
        var baseSize = style.FixedSizePixels ?? height * HeightFraction * style.SizeScale;

        using var font = new SKFont(typeface, baseSize);
        using var paint = new SKPaint { Color = color, IsAntialias = true };

        var textWidth = font.MeasureText(text, paint);

        // Auto-fitted text is still shrunk to the widget's width so nothing
        // bleeds outside its box. Fixed-size text deliberately skips this -
        // "24px" means 24px even if the box is narrow (the canvas clip in
        // FrameCompositor keeps any overflow inside the widget bounds) -
        // unless the caller opts into alwaysShrinkToFitWidth (GaugeWidget's
        // value text: it must never touch the ring around it, so an
        // explicitly chosen size is still a ceiling, not a guarantee).
        var maxTextWidth = width * MaxWidthFraction;
        if ((alwaysShrinkToFitWidth || style.FixedSizePixels is null) && textWidth > maxTextWidth)
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

        if (string.Equals(style.FontFamily, SpaceMonoFamilyName, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveSpaceMono(style.Bold, style.Italic);
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

    private static SKTypeface ResolveSpaceMono(bool bold, bool italic)
    {
        var key = (bold, italic);
        lock (SpaceMonoLock)
        {
            if (SpaceMonoTypefaces.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var fileName = (bold, italic) switch
            {
                (true, true) => "SpaceMono-BoldItalic.ttf",
                (true, false) => "SpaceMono-Bold.ttf",
                (false, true) => "SpaceMono-Italic.ttf",
                (false, false) => "SpaceMono-Regular.ttf",
            };

            // Falling back to the default typeface (rather than throwing) if
            // the embedded resource is somehow missing keeps a broken/edited
            // build degrading to "wrong font" instead of a render-loop crash.
            var typeface = LoadEmbeddedTypeface(fileName) ?? SKTypeface.Default;
            SpaceMonoTypefaces[key] = typeface;
            return typeface;
        }
    }

    private static SKTypeface? LoadEmbeddedTypeface(string fileName)
    {
        var resourceName = $"Ax206Display.Rendering.Fonts.SpaceMono.{fileName}";
        using var stream = typeof(WidgetTextRenderer).Assembly.GetManifestResourceStream(resourceName);
        return stream is null ? null : SKTypeface.FromStream(stream);
    }
}
