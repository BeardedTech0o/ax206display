using Ax206Display.DataSources.Network;
using Ax206Display.Rendering.Playback;

namespace Ax206Display.Tests.DataSources;

public class NetworkSpeedPublisherTests
{
    [Fact]
    public void Publish_ConvertsBytesPerSecondToMbps()
    {
        var hub = new RenderDataHub();
        var snapshot = new NetworkSpeedSnapshot
        {
            DownloadBytesPerSecond = 1_250_000,
            UploadBytesPerSecond = 125_000,
        };

        NetworkSpeedPublisher.Publish(snapshot, hub.Publish, hub.Remove);

        var data = hub.GetSnapshot();
        Assert.Equal(10.0, (double)data[NetworkSpeedKeys.DownloadMbps], precision: 5);
        Assert.Equal(1.0, (double)data[NetworkSpeedKeys.UploadMbps], precision: 5);
    }

    [Fact]
    public void Publish_NullReading_RemovesItsPreviouslyPublishedKey()
    {
        var hub = new RenderDataHub();
        NetworkSpeedPublisher.Publish(new NetworkSpeedSnapshot { DownloadBytesPerSecond = 500_000 }, hub.Publish, hub.Remove);

        // First call after a source restarts (no prior sample yet) reports nulls.
        NetworkSpeedPublisher.Publish(new NetworkSpeedSnapshot(), hub.Publish, hub.Remove);

        Assert.False(hub.GetSnapshot().ContainsKey(NetworkSpeedKeys.DownloadMbps));
    }
}
