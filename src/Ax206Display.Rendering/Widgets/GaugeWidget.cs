using System.Globalization;
using SkiaSharp;

namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// A compact circular arc gauge for one live numeric stat: a 270-degree arc
/// (open at the bottom, like a speedometer) fills from <see cref="_minValue"/>
/// to <see cref="_maxValue"/>, with the value centered inside it and an
/// optional smaller label underneath. Paints only the arc and text - no
/// background fill - so it composites over whatever background color/image
/// the device profile has, the same as every other widget.
/// </summary>
public sealed class GaugeWidget : IWidget
{
    public const string MissingValuePlaceholder = "--";

    private const float StartAngle = 135f;
    private const float SweepAngle = 270f;
    private const float TrackAlphaFraction = 0.25f;
    private const float StrokeWidthFraction = 0.11f;
    private const float PaddingFraction = 0.04f;
    private const float LabelAreaHeightFraction = 0.38f;

    private readonly string _dataKey;
    private readonly string _label;
    private readonly string _unit;
    private readonly int _decimals;
    private readonly double _minValue;
    private readonly double _maxValue;
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

        DrawArc(canvas, fraction);

        var valueText = value is { } v
            ? v.ToString("F" + _decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + _unit
            : MissingValuePlaceholder;

        DrawText(canvas, valueText);
    }

    private void DrawArc(SKCanvas canvas, double fraction)
    {
        var size = Math.Min(Width, Height);
        var strokeWidth = size * StrokeWidthFraction;
        var padding = strokeWidth / 2f + size * PaddingFraction;
        var diameter = Math.Max(0f, size - (padding * 2f));
        var left = (Width - diameter) / 2f;
        var top = (Height - diameter) / 2f;
        var rect = new SKRect(left, top, left + diameter, top + diameter);

        using var trackPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
            Color = _gaugeColor.WithAlpha((byte)(255 * TrackAlphaFraction)),
        };
        canvas.DrawArc(rect, StartAngle, SweepAngle, false, trackPaint);

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
        canvas.DrawArc(rect, StartAngle, SweepAngle * (float)fraction, false, valuePaint);
    }

    private void DrawText(SKCanvas canvas, string valueText)
    {
        var hasLabel = !string.IsNullOrEmpty(_label);
        var valueAreaHeight = hasLabel ? Height * (1f - LabelAreaHeightFraction) : Height;

        WidgetTextRenderer.DrawCentered(canvas, valueText, Width, (int)valueAreaHeight, _textColor, _fontStyle);

        if (!hasLabel)
        {
            return;
        }

        var labelAreaHeight = Height - valueAreaHeight;
        canvas.Save();
        canvas.Translate(0, valueAreaHeight);
        // Always auto-fit the label to its (much shorter) sub-box rather
        // than honoring a fixed pixel size meant for the big value text -
        // otherwise a large explicit font size would blow the label out to
        // the same size as the value it's labeling.
        var labelStyle = _fontStyle with { FixedSizePixels = null };
        WidgetTextRenderer.DrawCentered(canvas, _label, Width, (int)labelAreaHeight, _textColor, labelStyle);
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
