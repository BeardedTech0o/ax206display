namespace Ax206Display.DataSources.SystemMonitor;

public sealed record SystemStatsSnapshot
{
    public double? CpuLoadPercent { get; init; }

    public double? CpuTemperatureCelsius { get; init; }

    public double? MemoryUsedPercent { get; init; }

    public double? GpuLoadPercent { get; init; }

    public double? GpuTemperatureCelsius { get; init; }
}
