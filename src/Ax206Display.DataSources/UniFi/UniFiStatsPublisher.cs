namespace Ax206Display.DataSources.UniFi;

/// <summary>
/// Maps a <see cref="UniFiSiteStatus"/> onto render-data keys: total, LAN-only,
/// and WLAN-only connected client counts (num_user on the "lan"/"wlan"
/// subsystems), and WAN throughput (the "wan" subsystem's byte rates,
/// converted to Mbps).
/// </summary>
public static class UniFiStatsPublisher
{
    private const double BytesPerSecondToMbps = 8.0 / 1_000_000.0;

    public static void Publish(UniFiSiteStatus status, Action<string, object> publish)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(publish);

        var lanClientCount = status.Subsystems.Where(s => s.Subsystem == "lan").Sum(s => s.NumUser);
        var wlanClientCount = status.Subsystems.Where(s => s.Subsystem == "wlan").Sum(s => s.NumUser);
        publish(UniFiStatKeys.ClientCount, (double)(lanClientCount + wlanClientCount));
        publish(UniFiStatKeys.LanClientCount, (double)lanClientCount);
        publish(UniFiStatKeys.WlanClientCount, (double)wlanClientCount);

        var wan = status.Subsystems.FirstOrDefault(s => s.Subsystem == "wan");
        publish(UniFiStatKeys.WanDownloadMbps, (wan?.RxBytesPerSecond ?? 0) * BytesPerSecondToMbps);
        publish(UniFiStatKeys.WanUploadMbps, (wan?.TxBytesPerSecond ?? 0) * BytesPerSecondToMbps);
    }
}
