using Ax206Display.DataSources.SystemMonitor;

namespace Ax206Display.Tests.DataSources;

public class MockSystemMonitorSourceTests
{
    [Fact]
    public void GetSnapshot_ReturnsWhateverIsAssigned()
    {
        var source = new MockSystemMonitorSource
        {
            Snapshot = new SystemStatsSnapshot { CpuLoadPercent = 99 },
        };

        Assert.Equal(99, source.GetSnapshot().CpuLoadPercent);
    }
}
