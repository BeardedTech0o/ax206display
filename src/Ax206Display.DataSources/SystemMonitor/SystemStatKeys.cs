namespace Ax206Display.DataSources.SystemMonitor;

/// <summary>
/// The render-data keys the system-monitor pump publishes
/// <see cref="SystemStatsSnapshot"/> values under. Widget configs reference
/// these as their 'dataKey' setting, so treat them as a public contract:
/// renaming one breaks existing saved layouts.
/// </summary>
public static class SystemStatKeys
{
    public const string CpuLoadPercent = "system.cpu.load";
    public const string CpuTemperatureCelsius = "system.cpu.temp";
    public const string MemoryUsedPercent = "system.memory.used";
    public const string GpuLoadPercent = "system.gpu.load";
    public const string GpuTemperatureCelsius = "system.gpu.temp";
}
