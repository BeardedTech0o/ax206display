using Ax206Display.DataSources.SystemMonitor;
using Ax206Display.Rendering.Playback;

namespace Ax206Display.Tests.DataSources;

public class SystemStatsPublisherTests
{
    [Fact]
    public void Publish_FullSnapshot_PublishesEveryKey()
    {
        var hub = new RenderDataHub();
        var snapshot = new SystemStatsSnapshot
        {
            CpuLoadPercent = 42.5,
            CpuTemperatureCelsius = 61.0,
            MemoryUsedPercent = 70.1,
            GpuLoadPercent = 15.0,
            GpuTemperatureCelsius = 48.0,
        };

        SystemStatsPublisher.Publish(snapshot, hub.Publish, hub.Remove);

        var data = hub.GetSnapshot();
        Assert.Equal(42.5, data[SystemStatKeys.CpuLoadPercent]);
        Assert.Equal(61.0, data[SystemStatKeys.CpuTemperatureCelsius]);
        Assert.Equal(70.1, data[SystemStatKeys.MemoryUsedPercent]);
        Assert.Equal(15.0, data[SystemStatKeys.GpuLoadPercent]);
        Assert.Equal(48.0, data[SystemStatKeys.GpuTemperatureCelsius]);
    }

    [Fact]
    public void Publish_MissingSensor_RemovesItsPreviouslyPublishedKey()
    {
        var hub = new RenderDataHub();
        SystemStatsPublisher.Publish(
            new SystemStatsSnapshot { GpuTemperatureCelsius = 50.0, CpuLoadPercent = 10.0 },
            hub.Publish,
            hub.Remove);

        // GPU disappears (driver reset, sensor gone) - its key must go too.
        SystemStatsPublisher.Publish(
            new SystemStatsSnapshot { CpuLoadPercent = 12.0 },
            hub.Publish,
            hub.Remove);

        var data = hub.GetSnapshot();
        Assert.Equal(12.0, data[SystemStatKeys.CpuLoadPercent]);
        Assert.False(data.ContainsKey(SystemStatKeys.GpuTemperatureCelsius));
    }
}
