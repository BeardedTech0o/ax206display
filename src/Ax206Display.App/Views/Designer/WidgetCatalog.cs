using Ax206Display.DataSources.Network;
using Ax206Display.DataSources.PiHole;
using Ax206Display.DataSources.SystemMonitor;
using Ax206Display.DataSources.UniFi;
using Ax206Display.Rendering.Widgets;

namespace Ax206Display.App.Views.Designer;

/// <summary>
/// Everything the Widget Designer's UI needs to offer choices without the
/// user typing a widget type, data key, or color by hand.
/// </summary>
internal static class WidgetCatalog
{
    internal sealed record WidgetTypeDescriptor(string Type, string DisplayName);

    internal static readonly IReadOnlyList<WidgetTypeDescriptor> Types =
    [
        new("clock", "Clock"),
        new("text", "Text Label"),
        new("stat", "System Stat"),
    ];

    internal sealed record StatKeyDescriptor(string Key, string DisplayName, string DefaultLabel, string DefaultUnit);

    internal static readonly IReadOnlyList<StatKeyDescriptor> StatKeys =
    [
        new(SystemStatKeys.CpuLoadPercent, "CPU Load", "CPU", "%"),
        new(SystemStatKeys.CpuTemperatureCelsius, "CPU Temperature", "CPU", "°C"),
        new(SystemStatKeys.MemoryUsedPercent, "Memory Used", "RAM", "%"),
        new(SystemStatKeys.GpuLoadPercent, "GPU Load", "GPU", "%"),
        new(SystemStatKeys.GpuTemperatureCelsius, "GPU Temperature", "GPU", "°C"),
        new(NetworkSpeedKeys.DownloadMbps, "Network Download", "Down", " Mbps"),
        new(NetworkSpeedKeys.UploadMbps, "Network Upload", "Up", " Mbps"),
        new(PiHoleStatKeys.AdsBlockedToday, "Pi-hole: Ads Blocked Today", "Blocked", string.Empty),
        new(PiHoleStatKeys.AdsPercentageToday, "Pi-hole: Blocked Percentage", "Blocked", "%"),
        new(PiHoleStatKeys.DnsQueriesToday, "Pi-hole: DNS Queries Today", "Queries", string.Empty),
        new(UniFiStatKeys.ClientCount, "UniFi: Connected Clients", "Clients", string.Empty),
        new(UniFiStatKeys.WanDownloadMbps, "UniFi: WAN Download", "Down", " Mbps"),
        new(UniFiStatKeys.WanUploadMbps, "UniFi: WAN Upload", "Up", " Mbps"),
    ];

    internal sealed record ColorSwatch(string Name, string Hex);

    internal static readonly IReadOnlyList<ColorSwatch> Colors =
    [
        new("White", "#FFFFFF"),
        new("Silver", "#C7C7CC"),
        new("Red", "#FF3B30"),
        new("Orange", "#FF9500"),
        new("Yellow", "#FFCC00"),
        new("Gold", "#FFD60A"),
        new("Green", "#34C759"),
        new("Mint", "#00C7BE"),
        new("Teal", "#30B0C7"),
        new("Cyan", "#32ADE6"),
        new("Blue", "#007AFF"),
        new("Indigo", "#5856D6"),
        new("Purple", "#AF52DE"),
        new("Pink", "#FF2D95"),
        new("Magenta", "#FF375F"),
        new("Brown", "#A2845E"),
        new("Gray", "#8E8E93"),
    ];

    internal const string DefaultFontLabel = "Default";

    internal static readonly IReadOnlyList<string> FontFamilies =
    [
        DefaultFontLabel,
        "Segoe UI",
        "Arial",
        "Consolas",
        "Courier New",
        "Impact",
        "Times New Roman",
        "Verdana",
        "Trebuchet MS",
        "Georgia",
        "Comic Sans MS",
    ];

    /// <summary>Pixels = null is "Auto": fit the text to the widget's box, the historical behavior.</summary>
    internal sealed record FontSizeOption(string DisplayName, double? Pixels);

    internal static readonly IReadOnlyList<FontSizeOption> FontSizes = BuildFontSizes();

    private static List<FontSizeOption> BuildFontSizes()
    {
        // Sizes are in the AX206 panel's own pixels (the canvas the widgets
        // render onto), so "24 px" is 24 physical rows on the display.
        int[] pixelSizes = [8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64, 80, 96];

        var options = new List<FontSizeOption> { new("Auto (fit to box)", null) };
        options.AddRange(pixelSizes.Select(px => new FontSizeOption($"{px} px", px)));
        return options;
    }

    internal const string DefaultTimeFormat = "HH:mm:ss";

    internal static readonly IReadOnlyList<string> TimeFormats =
    [
        "HH:mm:ss",
        "HH:mm",
        "hh:mm tt",
        "hh:mm:ss tt",
    ];

    internal static WidgetDesignItem CreateDefault(string type, int canvasWidth, int canvasHeight, int nextZOrder)
    {
        var width = Math.Clamp(canvasWidth / 3, 20, canvasWidth);
        var height = Math.Clamp(canvasHeight / 4, 15, canvasHeight);
        var x = Math.Max(0, (canvasWidth - width) / 2);
        var y = Math.Max(0, (canvasHeight - height) / 2);

        var item = new WidgetDesignItem
        {
            Id = type + "-" + Guid.NewGuid().ToString("N")[..8],
            Type = type,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            ZOrder = nextZOrder,
        };

        switch (type)
        {
            case "stat":
                var defaultStat = StatKeys[0];
                item.SetSetting("dataKey", defaultStat.Key);
                item.SetSetting("label", defaultStat.DefaultLabel);
                item.SetSetting("unit", defaultStat.DefaultUnit);
                break;

            case "text":
                item.SetSetting("text", "Label");
                break;
        }

        return item;
    }
}
