namespace Ax206Display.DataSources.SystemMonitor;

/// <summary>Fixed/injectable snapshot source used by tests and the widget designer's live preview.</summary>
public sealed class MockSystemMonitorSource : ISystemMonitorSource
{
    public SystemStatsSnapshot Snapshot { get; set; } = new()
    {
        CpuLoadPercent = 42,
        CpuTemperatureCelsius = 55,
        MemoryUsedPercent = 61,
        GpuLoadPercent = 30,
        GpuTemperatureCelsius = 48,
    };

    public SystemStatsSnapshot GetSnapshot() => Snapshot;
}
