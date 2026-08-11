namespace Ax206Display.DataSources.UniFi;

/// <summary>
/// The render-data keys the UniFi pump publishes <see cref="UniFiSiteStatus"/>
/// values under. Widget configs reference these as their 'dataKey' setting -
/// treat them as a public contract, renaming one breaks existing saved layouts.
/// </summary>
public static class UniFiStatKeys
{
    public const string ClientCount = "unifi.clientCount";
    public const string WanDownloadMbps = "unifi.wan.download.mbps";
    public const string WanUploadMbps = "unifi.wan.upload.mbps";
    public const string LanClientCount = "unifi.lan.clientCount";
    public const string WlanClientCount = "unifi.wlan.clientCount";
}
