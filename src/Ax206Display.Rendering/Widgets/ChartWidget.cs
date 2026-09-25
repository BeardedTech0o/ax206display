using System.Globalization;
using SkiaSharp;

namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// A rolling line/area chart of one numeric render-data key's recent history
/// (<see cref="WidgetRenderContext.GetHistory"/>) - the panel counterpart to
/// the web dashboard's interactive charts, simplified because the panel has
/// no input: a single fixed time window, no range tabs, no hover. Min/max
/// auto-scale to the samples actually in view (padded slightly) rather than
/// a configured range, since most metrics this draws (network throughput,
/// gauge-style percentages already have GaugeWidget) don't have a single
/// natural fixed scale the way a gauge's 0-100% does.
/// </summary>
public sealed class ChartWidget : IWidget
{
    private const float OverlayHeightFraction = 0.22f;
    private const float LineStrokeWidthFraction = 0.02f;
    private const float AreaAlphaFraction = 0.22f;
    private const float RangePaddingFraction = 0.08f;

    private readonly string _dataKey;
    private readonly string _label;
    private readonly string _unit;
    private readonly int _decimals;
    private readonly TimeSpan _window;
    private readonly bool _showArea;
    private readonly SKColor _lineColor;
    private readonly SKColor _textColor;
    private readonly WidgetFontStyle _fontStyle;

    public ChartWidget(
        string id,
        int width,
        int height,
        string dataKey,
        string label = "",
        string unit = "",
        int decimals = 1,
        TimeSpan? window = null,
        bool showArea = true,
        SKColor? lineColor = null,
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
        _window = window is { } w && w > TimeSpan.Zero ? w : TimeSpan.FromMinutes(60);
        _showArea = showArea;
        _lineColor = lineColor ?? SKColors.White;
        _textColor = textColor ?? SKColors.White;
        _fontStyle = fontStyle ?? WidgetFontStyle.Default;
    }

    public string Id { get; }

    public int Width { get; }

    public int Height { get; }

    public void Render(SKCanvas canvas, WidgetRenderContext context)
    {
        var samples = context.GetHistory(_dataKey, _window);
        var overlayHeight = Height * OverlayHeightFraction;
        var plotTop = overlayHeight;
        var plotHeight = Height - overlayHeight;

        if (samples.Count < 2)
        {
            DrawOverlay(canvas, samples.Count == 1 ? samples[^1].Value : null, overlayHeight);
            DrawPlaceholder(canvas, plotTop, plotHeight);
            return;
        }

        var minValue = samples.Min(s => s.Value);
        var maxValue = samples.Max(s => s.Value);
        var range = maxValue - minValue;
        if (range <= 0)
        {
            // A perfectly flat series (e.g. constant 0%) would divide by
            // zero below - widen it slightly so the line still draws as a
            // flat mid-height line instead of degenerating.
            range = Math.Max(Math.Abs(maxValue) * 0.1, 1.0);
            minValue -= range / 2;
            maxValue += range / 2;
        }
        else
        {
            var padding = range * RangePaddingFraction;
            minValue -= padding;
            maxValue += padding;
            range = maxValue - minValue;
        }

        var startTime = samples[0].Timestamp;
        var endTime = samples[^1].Timestamp;
        var timeSpan = (endTime - startTime).TotalSeconds;

        using var path = new SKPath();
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var x = timeSpan > 0 ? (float)((sample.Timestamp - startTime).TotalSeconds / timeSpan) * Width : Width;
            var y = plotTop + plotHeight - (float)((sample.Value - minValue) / range) * plotHeight;

            if (i == 0)
            {
                path.MoveTo(x, y);
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        if (_showArea)
        {
            using var areaPath = new SKPath(path);
            areaPath.LineTo(Width, plotTop + plotHeight);
            areaPath.LineTo(0, plotTop + plotHeight);
            areaPath.Close();

            using var areaPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                Color = _lineColor.WithAlpha((byte)(255 * AreaAlphaFraction)),
            };
            canvas.DrawPath(areaPath, areaPaint);
        }

        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, Height * LineStrokeWidthFraction),
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
            Color = _lineColor,
        };
        canvas.DrawPath(path, linePaint);

        DrawOverlay(canvas, samples[^1].Value, overlayHeight);
    }

    /// <summary>Label + current value, left-aligned in a short strip above the plot - deliberately not WidgetTextRenderer.DrawCentered (built for a widget's whole box), since this needs to sit compactly in a corner without dominating the chart beneath it.</summary>
    private void DrawOverlay(SKCanvas canvas, double? currentValue, float overlayHeight)
    {
        if (string.IsNullOrEmpty(_label) && currentValue is null)
        {
            return;
        }

        var text = currentValue is { } value
            ? (string.IsNullOrEmpty(_label) ? "" : _label + " ") + value.ToString("F" + _decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + _unit
            : _label;

        var typeface = string.IsNullOrEmpty(_fontStyle.FontFamily)
            ? SKTypeface.Default
            : SKTypeface.FromFamilyName(_fontStyle.FontFamily, _fontStyle.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, _fontStyle.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        var fontSize = _fontStyle.FixedSizePixels ?? overlayHeight * 0.7f;
        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint { Color = _textColor, IsAntialias = true };

        var y = overlayHeight / 2f - (font.Metrics.Ascent + font.Metrics.Descent) / 2f;
        canvas.DrawText(text, 2, y, font, paint);
    }

    private void DrawPlaceholder(SKCanvas canvas, float plotTop, float plotHeight)
    {
        canvas.Save();
        canvas.Translate(0, plotTop);
        WidgetTextRenderer.DrawCentered(canvas, "No data yet", Width, (int)plotHeight, _textColor.WithAlpha(160), _fontStyle);
        canvas.Restore();
    }
}
