using Ax206Display.DataSources.PiHole;
using Ax206Display.Rendering.Playback;

namespace Ax206Display.Tests.DataSources;

public class PiHoleStatsPublisherTests
{
    [Fact]
    public void Publish_PublishesAllStats()
    {
        var hub = new RenderDataHub();
        var summary = new PiHoleSummary
        {
            AdsBlockedToday = 1234,
            AdsPercentageToday = 12.5,
            DnsQueriesToday = 9876,
            DomainsOnBlocklist = 150000,
            QueriesCached = 5000,
            QueriesForwarded = 4000,
            UniqueDomains = 543,
            ActiveClients = 8,
            TotalClients = 12,
        };

        PiHoleStatsPublisher.Publish(summary, hub.Publish);

        var data = hub.GetSnapshot();
        Assert.Equal(1234.0, (double)data[PiHoleStatKeys.AdsBlockedToday]);
        Assert.Equal(12.5, (double)data[PiHoleStatKeys.AdsPercentageToday]);
        Assert.Equal(9876.0, (double)data[PiHoleStatKeys.DnsQueriesToday]);
        Assert.Equal(150000.0, (double)data[PiHoleStatKeys.DomainsOnBlocklist]);
        Assert.Equal(5000.0, (double)data[PiHoleStatKeys.QueriesCached]);
        Assert.Equal(4000.0, (double)data[PiHoleStatKeys.QueriesForwarded]);
        Assert.Equal(543.0, (double)data[PiHoleStatKeys.UniqueDomains]);
        Assert.Equal(8.0, (double)data[PiHoleStatKeys.ActiveClients]);
        Assert.Equal(12.0, (double)data[PiHoleStatKeys.TotalClients]);
    }
}
