using System.Text.Json.Nodes;
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
            // fontScale is the legacy relative-size setting - still read so
            // layouts saved before fontSizePx existed render unchanged.
            SizeScale: (float)(ReadDouble(config.Settings["fontScale"]) ?? 1.0),
            FixedSizePixels: (float?)ReadDouble(config.Settings["fontSizePx"]));
    }

    /// <summary>
    /// Reads a numeric setting whether the node came from parsed JSON or was
    /// assigned in memory. GetValue&lt;double&gt;() alone throws for an
    /// in-memory JsonValue created from an int (no implicit numeric
    /// conversion) - which is exactly what the designer's live preview
    /// produces between assigning a setting and saving it to disk.
    /// </summary>
    private static double? ReadDouble(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var value = node.AsValue();
        if (value.TryGetValue<double>(out var asDouble))
        {
            return asDouble;
        }

        if (value.TryGetValue<int>(out var asInt))
        {
            return asInt;
        }

        return null;
    }
}
