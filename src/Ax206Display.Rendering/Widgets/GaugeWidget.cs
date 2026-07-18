using System.Globalization;
using SkiaSharp;

namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// A compact circular arc gauge for one live numeric stat: a 270-degree arc
/// (open at the bottom, like a speedometer) fills from <see cref="_minValue"/>
/// to <see cref="_maxValue"/>, with the value centered inside it. The
/// optional label sits in its own strip entirely below the ring - not the
/// arc's bottom gap - so neither text ever overlaps the stroke. Paints only
/// the arc and text - no background fill - so it composites over whatever
/// background color/image the device profile has, the same as every other
/// widget.
/// </summary>
public sealed class GaugeWidget : IWidget
{
    public const string MissingValuePlaceholder = "--";

    private const float StartAngle = 135f;
    private const float SweepAngle = 270f;
    private const float TrackAlphaFraction = 0.25f;
    private const float StrokeWidthFraction = 0.11f;
    private const float PaddingFraction = 0.04f;

    /// <summary>Fraction of Height reserved as a strip below the ring for the label - entirely separate from the circle, never overlapping it.</summary>
    private const float FooterHeightFraction = 0.26f;

    /// <summary>
    /// The value text is confined to a square this fraction of the ring's
    /// diameter, centered on it. The largest square that fits inside a
    /// circle without touching it is ~0.707x the diameter; using a smaller
    /// fraction leaves a visible margin so glyphs never brush the stroke.
    /// </summary>
    private const float SafeContentFraction = 0.6f;

    private readonly string _dataKey;
    private readonly string _label;
    private readonly string _unit;
    private readonly int _decimals;
    private readonly double _minValue;
    private readonly double _maxValue;
    private readonly float? _valueFontSizePx;
    private readonly SKColor _gaugeColor;
    private readonly SKColor _textColor;
    private readonly WidgetFontStyle _fontStyle;

    public GaugeWidget(
        string id,
        int width,
        int height,
        string dataKey,
        string label = "",
        string unit = "",
        int decimals = 0,
        double minValue = 0,
        double maxValue = 100,
        float? valueFontSizePx = null,
        SKColor? gaugeColor = null,
        SKColor? textColor = null,
        WidgetFontStyle? fontStyle = null)
    {
        Id = id;
        Width = width;
        Height = height;
        _dataKey = dataKey;
        _label = label;
        _unit = unit;
        _decimals = Math.Clamp(decimals, 0, 3);
        _minValue = minValue;
        // A degenerate or inverted range would divide by zero (or draw the
        // arc backwards) below - fall back to a valid one-wide range instead
        // of throwing, consistent with how SystemStatWidget tolerates bad input.
        _maxValue = maxValue > minValue ? maxValue : minValue + 1;
        _valueFontSizePx = valueFontSizePx;
        _gaugeColor = gaugeColor ?? SKColors.White;
        _textColor = textColor ?? SKColors.White;
        _fontStyle = fontStyle ?? WidgetFontStyle.Default;
    }

    public string Id { get; }

    public int Width { get; }

    public int Height { get; }

    public void Render(SKCanvas canvas, WidgetRenderContext context)
    {
        var value = AsDouble(context, _dataKey);
        var fraction = value is { } number
            ? Math.Clamp((number - _minValue) / (_maxValue - _minValue), 0.0, 1.0)
            : 0.0;

        var hasLabel = !string.IsNullOrEmpty(_label);
        var footerHeight = hasLabel ? Height * FooterHeightFraction : 0f;
        var circleAreaHeight = Height - footerHeight;

        var (ring, strokeWidth) = ComputeRing(circleAreaHeight);
        DrawArc(canvas, ring, strokeWidth, fraction);

        var valueText = value is { } v
            ? v.ToString("F" + _decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + _unit
            : MissingValuePlaceholder;
        DrawValue(canvas, ring, valueText);

        if (hasLabel)
        {
            DrawLabel(canvas, circleAreaHeight, footerHeight);
        }
    }

    /// <summary>The square the ring is inscribed in (plus its stroke width), centered within the top circleAreaHeight-tall region - the footer, if any, is excluded entirely.</summary>
    private (SKRect Rect, float StrokeWidth) ComputeRing(float circleAreaHeight)
    {
        var size = Math.Min(Width, circleAreaHeight);
        var strokeWidth = size * StrokeWidthFraction;
        var padding = strokeWidth / 2f + size * PaddingFraction;
        var diameter = Math.Max(0f, size - (padding * 2f));
        var left = (Width - diameter) / 2f;
        var top = (circleAreaHeight - diameter) / 2f;
        return (new SKRect(left, top, left + diameter, top + diameter), strokeWidth);
    }

    private void DrawArc(SKCanvas canvas, SKRect ring, float strokeWidth, double fraction)
    {
        using var trackPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
            Color = _gaugeColor.WithAlpha((byte)(255 * TrackAlphaFraction)),
        };
        canvas.DrawArc(ring, StartAngle, SweepAngle, false, trackPaint);

        if (fraction <= 0)
        {
            return;
        }

        using var valuePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
            Color = _gaugeColor,
        };
        canvas.DrawArc(ring, StartAngle, SweepAngle * (float)fraction, false, valuePaint);
    }

    /// <summary>
    /// Draws the value inside a square centered on the ring, sized well
    /// short of the ring's diameter so it can never touch the stroke - see
    /// <see cref="SafeContentFraction"/>. An explicit value text size is
    /// still clamped to that safe square (both dimensions, via
    /// alwaysShrinkToFitWidth) rather than honored literally: unlike a plain
    /// rectangular widget, letting a big chosen size bleed past the box here
    /// would mean it visibly overlaps the ring around it.
    /// </summary>
    private void DrawValue(SKCanvas canvas, SKRect ring, string valueText)
    {
        var safeSide = Math.Min(ring.Width, ring.Height) * SafeContentFraction;
        var safeLeft = ring.Left + (ring.Width - safeSide) / 2f;
        var safeTop = ring.Top + (ring.Height - safeSide) / 2f;

        var clampedSizePx = _valueFontSizePx is { } explicitSize
            ? Math.Min(explicitSize, safeSide)
            : (float?)null;
        var valueStyle = _fontStyle with { FixedSizePixels = clampedSizePx };

        canvas.Save();
        canvas.Translate(safeLeft, safeTop);
        WidgetTextRenderer.DrawCentered(canvas, valueText, (int)safeSide, (int)safeSide, _textColor, valueStyle, alwaysShrinkToFitWidth: true);
        canvas.Restore();
    }

    /// <summary>
    /// The label's own strip, entirely below circleAreaHeight - physically
    /// separate from the ring's bounding square, so it never overlaps the
    /// arc regardless of how tight the ring's own padding is. Sized/styled
    /// the same way any other widget's single line of text would be (the
    /// shared Font/Size controls in the property panel apply here, not to
    /// the value - see <see cref="_valueFontSizePx"/> for that one).
    /// </summary>
    private void DrawLabel(SKCanvas canvas, float circleAreaHeight, float footerHeight)
    {
        canvas.Save();
        canvas.Translate(0, circleAreaHeight);
        WidgetTextRenderer.DrawCentered(canvas, _label, Width, (int)footerHeight, _textColor, _fontStyle);
        canvas.Restore();
    }

    private static double? AsDouble(WidgetRenderContext context, string key)
    {
        return context.Data.TryGetValue(key, out var raw)
            ? raw switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                _ => null,
            }
            : null;
    }
}
