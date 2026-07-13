namespace Ax206Display.DataSources.Network;

/// <summary>Maps a <see cref="NetworkSpeedSnapshot"/> onto render-data keys, converting bytes/sec to Mbps.</summary>
public static class NetworkSpeedPublisher
{
    private const double BytesPerSecondToMbps = 8.0 / 1_000_000.0;

    public static void Publish(NetworkSpeedSnapshot snapshot, Action<string, object> publish, Action<string> remove)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(remove);

        PublishOne(NetworkSpeedKeys.DownloadMbps, snapshot.DownloadBytesPerSecond, publish, remove);
        PublishOne(NetworkSpeedKeys.UploadMbps, snapshot.UploadBytesPerSecond, publish, remove);
    }

    private static void PublishOne(string key, double? bytesPerSecond, Action<string, object> publish, Action<string> remove)
    {
        if (bytesPerSecond is { } value)
        {
            publish(key, value * BytesPerSecondToMbps);
        }
        else
        {
            remove(key);
        }
    }
}
