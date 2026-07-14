using Ax206Display.DataSources.UniFi;
using Ax206Display.Rendering.Playback;

namespace Ax206Display.Tests.DataSources;

public class UniFiStatsPublisherTests
{
    [Fact]
    public void Publish_SumsClientCountAcrossLanAndWlanAndConvertsWanRatesToMbps()
    {
        var hub = new RenderDataHub();
        var status = new UniFiSiteStatus(
        [
            new UniFiSubsystemHealth("wan", "ok", RxBytesPerSecond: 1_250_000, TxBytesPerSecond: 375_000),
            new UniFiSubsystemHealth("lan", "ok", NumUser: 5),
            new UniFiSubsystemHealth("wlan", "ok", NumUser: 12),
            new UniFiSubsystemHealth("www", "ok"),
        ]);

        UniFiStatsPublisher.Publish(status, hub.Publish);

        var data = hub.GetSnapshot();
        Assert.Equal(17.0, (double)data[UniFiStatKeys.ClientCount]);
        Assert.Equal(10.0, (double)data[UniFiStatKeys.WanDownloadMbps]);
        Assert.Equal(3.0, (double)data[UniFiStatKeys.WanUploadMbps]);
    }

    [Fact]
    public void Publish_NoWanSubsystem_ReportsZeroThroughputInsteadOfThrowing()
    {
        var hub = new RenderDataHub();
        var status = new UniFiSiteStatus([new UniFiSubsystemHealth("lan", "ok", NumUser: 3)]);

        UniFiStatsPublisher.Publish(status, hub.Publish);

        var data = hub.GetSnapshot();
        Assert.Equal(3.0, (double)data[UniFiStatKeys.ClientCount]);
        Assert.Equal(0.0, (double)data[UniFiStatKeys.WanDownloadMbps]);
        Assert.Equal(0.0, (double)data[UniFiStatKeys.WanUploadMbps]);
    }
}
