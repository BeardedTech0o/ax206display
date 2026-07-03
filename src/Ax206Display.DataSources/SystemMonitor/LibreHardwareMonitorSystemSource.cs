using LibreHardwareMonitor.Hardware;

namespace Ax206Display.DataSources.SystemMonitor;

/// <summary>
/// Reads CPU/GPU/memory sensors via LibreHardwareMonitorLib. Most sensors
/// require running elevated on Windows, which matches this app's normal
/// (elevated, Task-Scheduler-launched) execution mode.
/// </summary>
public sealed class LibreHardwareMonitorSystemSource : ISystemMonitorSource, IDisposable
{
    private readonly Computer _computer;

    public LibreHardwareMonitorSystemSource()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
        };
        _computer.Open();
    }

    public SystemStatsSnapshot GetSnapshot()
    {
        double? cpuLoad = null;
        double? cpuTemp = null;
        double? memoryUsed = null;
        double? gpuLoad = null;
        double? gpuTemp = null;

        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();

            switch (hardware.HardwareType)
            {
                case HardwareType.Cpu:
                    cpuLoad ??= FindSensorValue(hardware, SensorType.Load, "CPU Total");
                    cpuTemp ??= FindSensorValue(hardware, SensorType.Temperature, "CPU Package") ?? FindFirstSensorValue(hardware, SensorType.Temperature);
                    break;

                case HardwareType.Memory:
                    memoryUsed ??= FindSensorValue(hardware, SensorType.Load, "Memory");
                    break;

                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    gpuLoad ??= FindFirstSensorValue(hardware, SensorType.Load);
                    gpuTemp ??= FindFirstSensorValue(hardware, SensorType.Temperature);
                    break;
            }
        }

        return new SystemStatsSnapshot
        {
            CpuLoadPercent = cpuLoad,
            CpuTemperatureCelsius = cpuTemp,
            MemoryUsedPercent = memoryUsed,
            GpuLoadPercent = gpuLoad,
            GpuTemperatureCelsius = gpuTemp,
        };
    }

    private static double? FindSensorValue(IHardware hardware, SensorType type, string nameContains)
    {
        return hardware.Sensors
            .FirstOrDefault(s => s.SensorType == type && s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static double? FindFirstSensorValue(IHardware hardware, SensorType type)
    {
        return hardware.Sensors.FirstOrDefault(s => s.SensorType == type)?.Value;
    }

    public void Dispose()
    {
        _computer.Close();
    }
}
