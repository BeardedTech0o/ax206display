using Ax206Display.Config.Models;
using SkiaSharp;

namespace Ax206Display.Rendering.Widgets;

/// <summary>
/// Builds an <see cref="IWidget"/> instance from its persisted <see cref="WidgetConfig"/>.
/// Adding a new widget type means adding a case here and a corresponding
/// <see cref="WidgetConfig.Type"/> discriminator - the config schema itself
/// doesn't change (see <see cref="WidgetConfig.Settings"/>).
/// </summary>
public static class WidgetFactory
{
    public static IWidget Create(WidgetConfig config)
    {
        return config.Type switch
        {
            "clock" => CreateClock(config),
            "text" => CreateText(config),
            "stat" => CreateStat(config),
            _ => throw new NotSupportedException($"Unknown widget type '{config.Type}' (widget id '{config.Id}')."),
        };
    }

    private static ClockWidget CreateClock(WidgetConfig config)
    {
        var timeFormat = config.Settings["timeFormat"]?.GetValue<string>() ?? "HH:mm:ss";
        return new ClockWidget(config.Id, config.Width, config.Height, timeFormat, ReadTextColor(config), ReadFontStyle(config));
    }

    private static TextWidget CreateText(WidgetConfig config)
    {
        var text = config.Settings["text"]?.GetValue<string>() ?? string.Empty;
        return new TextWidget(config.Id, config.Width, config.Height, text, ReadTextColor(config), ReadFontStyle(config));
    }

    private static SystemStatWidget CreateStat(WidgetConfig config)
    {
        var dataKey = config.Settings["dataKey"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Stat widget '{config.Id}' is missing the required 'dataKey' setting.");

        var label = config.Settings["label"]?.GetValue<string>() ?? string.Empty;
        var unit = config.Settings["unit"]?.GetValue<string>() ?? string.Empty;
        var decimals = config.Settings["decimals"]?.GetValue<int>() ?? 0;

        return new SystemStatWidget(config.Id, config.Width, config.Height, dataKey, label, unit, decimals, ReadTextColor(config), ReadFontStyle(config));
    }

    private static SKColor? ReadTextColor(WidgetConfig config)
    {
        if (config.Settings["textColor"]?.GetValue<string>() is { } textColorHex && SKColor.TryParse(textColorHex, out var parsedColor))
        {
            return parsedColor;
        }

        return null;
    }

    private static WidgetFontStyle ReadFontStyle(WidgetConfig config)
    {
        return new WidgetFontStyle(
            FontFamily: config.Settings["fontFamily"]?.GetValue<string>(),
            Bold: config.Settings["bold"]?.GetValue<bool>() ?? false,
            Italic: config.Settings["italic"]?.GetValue<bool>() ?? false,
            SizeScale: (float)(config.Settings["fontScale"]?.GetValue<double>() ?? 1.0));
    }
}
