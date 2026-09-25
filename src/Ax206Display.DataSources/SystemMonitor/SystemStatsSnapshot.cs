namespace Ax206Display.DataSources.SystemMonitor;

public sealed record SystemStatsSnapshot
{
    public double? CpuLoadPercent { get; init; }

    public double? CpuTemperatureCelsius { get; init; }

    public double? MemoryUsedPercent { get; init; }

    /// <summary>Root/system volume usage - null on sources that don't report it (currently only LinuxSystemMonitorSource does).</summary>
    public double? DiskUsedPercent { get; init; }

    public double? GpuLoadPercent { get; init; }

    public double? GpuTemperatureCelsius { get; init; }
}
