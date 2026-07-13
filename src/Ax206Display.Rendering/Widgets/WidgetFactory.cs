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
            _ => throw new NotSupportedException($"Unknown widget type '{config.Type}' (widget id '{config.Id}')."),
        };
    }

    private static ClockWidget CreateClock(WidgetConfig config)
    {
        var timeFormat = config.Settings["timeFormat"]?.GetValue<string>() ?? "HH:mm:ss";

        SKColor? textColor = null;
        if (config.Settings["textColor"]?.GetValue<string>() is { } textColorHex && SKColor.TryParse(textColorHex, out var parsedColor))
        {
            textColor = parsedColor;
        }

        return new ClockWidget(config.Id, config.Width, config.Height, timeFormat, textColor);
    }
}
