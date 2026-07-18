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
        new("gauge", "Gauge"),
    ];

    internal const string CategoryLocalDevice = "Local Device";
    internal const string CategoryNetwork = "Network";
    internal const string CategoryPiHole = "Pi-hole";
    internal const string CategoryUniFi = "UniFi";
    internal const string CategoryProxmox = "Proxmox";

    internal sealed record StatKeyDescriptor(string Key, string Category, string DisplayName, string DefaultLabel, string DefaultUnit);

    internal static readonly IReadOnlyList<StatKeyDescriptor> StatKeys =
    [
        new(SystemStatKeys.CpuLoadPercent, CategoryLocalDevice, "CPU Load", "CPU", "%"),
        new(SystemStatKeys.CpuTemperatureCelsius, CategoryLocalDevice, "CPU Temperature", "CPU", "°C"),
        new(SystemStatKeys.MemoryUsedPercent, CategoryLocalDevice, "Memory Used", "RAM", "%"),
        new(SystemStatKeys.GpuLoadPercent, CategoryLocalDevice, "GPU Load", "GPU", "%"),
        new(SystemStatKeys.GpuTemperatureCelsius, CategoryLocalDevice, "GPU Temperature", "GPU", "°C"),
        new(NetworkSpeedKeys.DownloadMbps, CategoryNetwork, "Network Download", "Down", " Mbps"),
        new(NetworkSpeedKeys.UploadMbps, CategoryNetwork, "Network Upload", "Up", " Mbps"),
        new(PiHoleStatKeys.AdsBlockedToday, CategoryPiHole, "Ads Blocked Today", "Blocked", string.Empty),
        new(PiHoleStatKeys.AdsPercentageToday, CategoryPiHole, "Blocked Percentage", "Blocked", "%"),
        new(PiHoleStatKeys.DnsQueriesToday, CategoryPiHole, "DNS Queries Today", "Queries", string.Empty),
        new(PiHoleStatKeys.DomainsOnBlocklist, CategoryPiHole, "Domains on Blocklist", "Blocklist", string.Empty),
        new(PiHoleStatKeys.QueriesCached, CategoryPiHole, "Queries Cached", "Cached", string.Empty),
        new(PiHoleStatKeys.QueriesForwarded, CategoryPiHole, "Queries Forwarded", "Forwarded", string.Empty),
        new(PiHoleStatKeys.UniqueDomains, CategoryPiHole, "Unique Domains", "Domains", string.Empty),
        new(PiHoleStatKeys.ActiveClients, CategoryPiHole, "Active Clients", "Active", string.Empty),
        new(PiHoleStatKeys.TotalClients, CategoryPiHole, "Total Clients", "Clients", string.Empty),
        new(UniFiStatKeys.ClientCount, CategoryUniFi, "Connected Clients", "Clients", string.Empty),
        new(UniFiStatKeys.LanClientCount, CategoryUniFi, "LAN Clients", "LAN", string.Empty),
        new(UniFiStatKeys.WlanClientCount, CategoryUniFi, "WLAN Clients", "WLAN", string.Empty),
        new(UniFiStatKeys.WanDownloadMbps, CategoryUniFi, "WAN Download", "Down", " Mbps"),
        new(UniFiStatKeys.WanUploadMbps, CategoryUniFi, "WAN Upload", "Up", " Mbps"),
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
        "Space Mono",
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
        int width, height;
        if (type == "gauge")
        {
            // A gauge reads best roughly square - the arc gets clipped
            // toward a circle either way (see GaugeWidget), but starting
            // square avoids handing the user a visibly squashed default.
            var side = Math.Clamp(Math.Min(canvasWidth, canvasHeight) / 2, 20, Math.Min(canvasWidth, canvasHeight));
            width = side;
            height = side;
        }
        else
        {
            width = Math.Clamp(canvasWidth / 3, 20, canvasWidth);
            height = Math.Clamp(canvasHeight / 4, 15, canvasHeight);
        }

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
            case "gauge":
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
