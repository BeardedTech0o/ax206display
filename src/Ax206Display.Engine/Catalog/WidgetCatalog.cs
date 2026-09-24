using Ax206Display.DataSources.Network;
using Ax206Display.DataSources.PiHole;
using Ax206Display.DataSources.Proxmox;
using Ax206Display.DataSources.SystemMonitor;
using Ax206Display.DataSources.UniFi;
using Ax206Display.Rendering.Widgets;

namespace Ax206Display.Engine.Catalog;

/// <summary>
/// Everything a layout editor (the WPF Widget Designer, the web UI) needs to
/// offer choices without the user typing a widget type, data key, or color
/// by hand.
/// </summary>
public static class WidgetCatalog
{
    public sealed record WidgetTypeDescriptor(string Type, string DisplayName);

    public static readonly IReadOnlyList<WidgetTypeDescriptor> Types =
    [
        new("clock", "Clock"),
        new("text", "Text Label"),
        new("stat", "System Stat"),
        new("gauge", "Gauge"),
    ];

    public const string CategoryLocalDevice = "Local Device";
    public const string CategoryNetwork = "Network";
    public const string CategoryPiHole = "Pi-hole";
    public const string CategoryUniFi = "UniFi";
    public const string CategoryProxmox = "Proxmox";

    public sealed record StatKeyDescriptor(string Key, string Category, string DisplayName, string DefaultLabel, string DefaultUnit);

    public static readonly IReadOnlyList<StatKeyDescriptor> StatKeys =
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

    public sealed record ColorSwatch(string Name, string Hex);

    public static readonly IReadOnlyList<ColorSwatch> Colors =
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

    public const string DefaultFontLabel = "Default";

    public static readonly IReadOnlyList<string> FontFamilies =
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
    public sealed record FontSizeOption(string DisplayName, double? Pixels);

    public static readonly IReadOnlyList<FontSizeOption> FontSizes = BuildFontSizes();

    private static List<FontSizeOption> BuildFontSizes()
    {
        // Sizes are in the AX206 panel's own pixels (the canvas the widgets
        // render onto), so "24 px" is 24 physical rows on the display.
        int[] pixelSizes = [8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64, 80, 96];

        var options = new List<FontSizeOption> { new("Auto (fit to box)", null) };
        options.AddRange(pixelSizes.Select(px => new FontSizeOption($"{px} px", px)));
        return options;
    }

    /// <summary>
    /// The fixed system/network/integration keys plus one entry per
    /// currently-known Proxmox node (CPU/memory/uptime) and guest
    /// (CPU/memory) - read from ProxmoxNodeDirectory/ProxmoxGuestDirectory,
    /// plain in-memory snapshots the pump service keeps current, so listing
    /// them never makes a network call of its own.
    /// </summary>
    public static List<StatKeyDescriptor> BuildAvailableStatKeys(ProxmoxNodeDirectory nodeDirectory, ProxmoxGuestDirectory guestDirectory)
    {
        ArgumentNullException.ThrowIfNull(nodeDirectory);
        ArgumentNullException.ThrowIfNull(guestDirectory);

        var keys = new List<StatKeyDescriptor>(StatKeys);

        foreach (var node in nodeDirectory.GetSnapshot())
        {
            keys.Add(new StatKeyDescriptor(
                ProxmoxNodeKeys.CpuUsedPercent(node.Node),
                CategoryProxmox,
                $"{node.Node} CPU",
                node.Node,
                "%"));
            keys.Add(new StatKeyDescriptor(
                ProxmoxNodeKeys.MemoryUsedPercent(node.Node),
                CategoryProxmox,
                $"{node.Node} Memory",
                node.Node,
                "%"));
            keys.Add(new StatKeyDescriptor(
                ProxmoxNodeKeys.UptimeDays(node.Node),
                CategoryProxmox,
                $"{node.Node} Uptime",
                node.Node,
                " days"));
        }

        foreach (var guest in guestDirectory.GetSnapshot())
        {
            keys.Add(new StatKeyDescriptor(
                ProxmoxGuestKeys.CpuUsedPercent(guest.VmId),
                CategoryProxmox,
                $"{guest.Name} CPU",
                guest.Name,
                "%"));
            keys.Add(new StatKeyDescriptor(
                ProxmoxGuestKeys.MemoryUsedPercent(guest.VmId),
                CategoryProxmox,
                $"{guest.Name} Memory",
                guest.Name,
                "%"));
        }

        return keys;
    }

    public const string DefaultTimeFormat = "HH:mm:ss";

    public static readonly IReadOnlyList<string> TimeFormats =
    [
        "HH:mm:ss",
        "HH:mm",
        "hh:mm tt",
        "hh:mm:ss tt",
    ];

    public static WidgetDesignItem CreateDefault(string type, int canvasWidth, int canvasHeight, int nextZOrder)
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
