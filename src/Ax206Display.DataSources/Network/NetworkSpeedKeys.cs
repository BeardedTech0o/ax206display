namespace Ax206Display.DataSources.Network;

/// <summary>
/// The render-data keys the network speed pump publishes
/// <see cref="NetworkSpeedSnapshot"/> values under, in Mbps. Widget configs
/// reference these as their 'dataKey' setting - treat them as a public
/// contract, renaming one breaks existing saved layouts.
/// </summary>
public static class NetworkSpeedKeys
{
    public const string DownloadMbps = "network.download.mbps";
    public const string UploadMbps = "network.upload.mbps";
}
