using Ax206Display.DataSources.Pbs;
using Ax206Display.Rendering.Playback;

namespace Ax206Display.Tests.DataSources;

public class PbsStatsPublisherTests
{
    [Fact]
    public void Publish_PublishesEachDatastoreUnderItsOwnKey()
    {
        var hub = new RenderDataHub();
        var datastores = new List<PbsDatastoreStatus>
        {
            new() { Store = "backup", TotalBytes = 1_000_000_000, UsedBytes = 940_000_000, AvailableBytes = 60_000_000 },
        };

        PbsStatsPublisher.Publish("host-1", datastores, hub.Publish);

        Assert.Equal(94.0, (double)hub.GetSnapshot()[PbsStatKeys.UsedPercent("host-1", "backup")]);
    }

    [Fact]
    public void Publish_TwoHostsWithSameStoreName_DoNotCollide()
    {
        var hub = new RenderDataHub();
        var hostAStore = new List<PbsDatastoreStatus> { new() { Store = "backup", TotalBytes = 100, UsedBytes = 10 } };
        var hostBStore = new List<PbsDatastoreStatus> { new() { Store = "backup", TotalBytes = 100, UsedBytes = 90 } };

        PbsStatsPublisher.Publish("host-a", hostAStore, hub.Publish);
        PbsStatsPublisher.Publish("host-b", hostBStore, hub.Publish);

        Assert.Equal(10.0, (double)hub.GetSnapshot()[PbsStatKeys.UsedPercent("host-a", "backup")]);
        Assert.Equal(90.0, (double)hub.GetSnapshot()[PbsStatKeys.UsedPercent("host-b", "backup")]);
    }

    [Fact]
    public void Publish_ZeroTotal_DoesNotDivideByZero()
    {
        var hub = new RenderDataHub();
        var datastores = new List<PbsDatastoreStatus> { new() { Store = "empty", TotalBytes = 0 } };

        PbsStatsPublisher.Publish("host-1", datastores, hub.Publish);

        Assert.Equal(0.0, (double)hub.GetSnapshot()[PbsStatKeys.UsedPercent("host-1", "empty")]);
    }
}
