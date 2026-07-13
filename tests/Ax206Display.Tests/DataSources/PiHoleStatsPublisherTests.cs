using Ax206Display.DataSources.PiHole;
using Ax206Display.Rendering.Playback;

namespace Ax206Display.Tests.DataSources;

public class PiHoleStatsPublisherTests
{
    [Fact]
    public void Publish_PublishesAllThreeStats()
    {
        var hub = new RenderDataHub();
        var summary = new PiHoleSummary
        {
            Status = "enabled",
            AdsBlockedToday = 1234,
            AdsPercentageToday = 12.5,
            DnsQueriesToday = 9876,
        };

        PiHoleStatsPublisher.Publish(summary, hub.Publish);

        var data = hub.GetSnapshot();
        Assert.Equal(1234.0, (double)data[PiHoleStatKeys.AdsBlockedToday]);
        Assert.Equal(12.5, (double)data[PiHoleStatKeys.AdsPercentageToday]);
        Assert.Equal(9876.0, (double)data[PiHoleStatKeys.DnsQueriesToday]);
    }
}
