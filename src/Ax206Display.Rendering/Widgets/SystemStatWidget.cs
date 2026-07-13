using System.Globalization;
using SkiaSharp;

namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// Shows one live numeric stat ("CPU 43%", "GPU 61°C") read from
/// <see cref="WidgetRenderContext.Data"/> by key. Renders a placeholder value
/// when the key is absent - a sensor that needs elevation, hardware that
/// doesn't exist, or a pump that hasn't published yet - so the layout stays
/// stable instead of flickering.
/// </summary>
public sealed class SystemStatWidget : IWidget
{
    public const string MissingValuePlaceholder = "--";

    private readonly string _dataKey;
    private readonly string _label;
    private readonly string _unit;
    private readonly int _decimals;
    private readonly SKColor _textColor;

    public SystemStatWidget(
        string id,
        int width,
        int height,
        string dataKey,
        string label = "",
        string unit = "",
        int decimals = 0,
        SKColor? textColor = null)
    {
        Id = id;
        Width = width;
        Height = height;
        _dataKey = dataKey;
        _label = label;
        _unit = unit;
        _decimals = Math.Clamp(decimals, 0, 3);
        _textColor = textColor ?? SKColors.White;
    }

    public string Id { get; }

    public int Width { get; }

    public int Height { get; }

    public void Render(SKCanvas canvas, WidgetRenderContext context)
    {
        var value = AsDouble(context, _dataKey);

        var valueText = value is { } number
            ? number.ToString("F" + _decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : MissingValuePlaceholder;

        var text = string.IsNullOrEmpty(_label)
            ? valueText + _unit
            : _label + " " + valueText + _unit;

        WidgetTextRenderer.DrawCentered(canvas, text, Width, Height, _textColor);
    }

    private static double? AsDouble(WidgetRenderContext context, string key)
    {
        // Pumps publish doubles, but config round-trips or future sources may
        // hand over other numeric types - accept any of them.
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
