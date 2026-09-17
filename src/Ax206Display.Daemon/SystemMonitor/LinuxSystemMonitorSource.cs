using Ax206Display.DataSources.SystemMonitor;

namespace Ax206Display.Daemon.SystemMonitor;

/// <summary>
/// Linux counterpart to DataSources' LibreHardwareMonitorSystemSource, which
/// only ships Windows native runtime assets - it throws
/// FileNotFoundException the instant it's constructed on Linux. Reads the
/// same stats straight from procfs/sysfs instead, no native dependency:
/// CPU load from /proc/stat (delta between two samples, so the first call
/// after startup always returns null), temperature from the first
/// CPU-labelled zone under /sys/class/thermal (absent entirely inside most
/// VMs, including a Proxmox guest - falls back to null, same as a sensor a
/// physical box doesn't have), and memory from /proc/meminfo's
/// MemAvailable/MemTotal (matches what `free` reports, unlike the
/// naive MemFree calculation). GPU stats are left null: there's no
/// dependency-free way to read them for an arbitrary GPU vendor on Linux.
/// </summary>
public sealed class LinuxSystemMonitorSource : ISystemMonitorSource
{
    private const string ProcStatPath = "/proc/stat";
    private const string ProcMemInfoPath = "/proc/meminfo";
    private const string ThermalZoneGlobPath = "/sys/class/thermal";

    private (long IdleJiffies, long TotalJiffies)? _previousCpuSample;

    public SystemStatsSnapshot GetSnapshot()
    {
        return new SystemStatsSnapshot
        {
            CpuLoadPercent = TryReadCpuLoadPercent(),
            CpuTemperatureCelsius = TryReadCpuTemperatureCelsius(),
            MemoryUsedPercent = TryReadMemoryUsedPercent(),
        };
    }

    private double? TryReadCpuLoadPercent()
    {
        try
        {
            var line = File.ReadLines(ProcStatPath).First(l => l.StartsWith("cpu ", StringComparison.Ordinal));
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(f => long.Parse(f, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

            // user, nice, system, idle, iowait, irq, softirq, steal, guest, guest_nice.
            var idle = fields[3] + (fields.Length > 4 ? fields[4] : 0);
            var total = fields.Sum();

            var previous = _previousCpuSample;
            _previousCpuSample = (idle, total);
            if (previous is not { } sample)
            {
                return null;
            }

            var idleDelta = idle - sample.IdleJiffies;
            var totalDelta = total - sample.TotalJiffies;
            return totalDelta <= 0 ? null : 100.0 * (1.0 - ((double)idleDelta / totalDelta));
        }
        catch (Exception ex) when (ex is IOException or FormatException or OverflowException or InvalidOperationException)
        {
            return null;
        }
    }

    private static double? TryReadCpuTemperatureCelsius()
    {
        try
        {
            if (!Directory.Exists(ThermalZoneGlobPath))
            {
                return null;
            }

            foreach (var zoneDirectory in Directory.EnumerateDirectories(ThermalZoneGlobPath, "thermal_zone*"))
            {
                var typePath = Path.Combine(zoneDirectory, "type");
                var tempPath = Path.Combine(zoneDirectory, "temp");
                if (!File.Exists(tempPath) || !File.Exists(typePath))
                {
                    continue;
                }

                var zoneType = File.ReadAllText(typePath).Trim();
                var looksLikeCpu = zoneType.Contains("cpu", StringComparison.OrdinalIgnoreCase)
                    || zoneType.Contains("x86_pkg_temp", StringComparison.OrdinalIgnoreCase)
                    || zoneType.Contains("coretemp", StringComparison.OrdinalIgnoreCase);

                if (looksLikeCpu && long.TryParse(File.ReadAllText(tempPath).Trim(), out var milliCelsius))
                {
                    return milliCelsius / 1000.0;
                }
            }

            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static double? TryReadMemoryUsedPercent()
    {
        try
        {
            long? memTotalKb = null;
            long? memAvailableKb = null;

            foreach (var line in File.ReadLines(ProcMemInfoPath))
            {
                var separatorIndex = line.IndexOf(':');
                if (separatorIndex < 0)
                {
                    continue;
                }

                var key = line[..separatorIndex];
                if (key != "MemTotal" && key != "MemAvailable")
                {
                    continue;
                }

                var valueField = line[(separatorIndex + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                if (!long.TryParse(valueField, out var kilobytes))
                {
                    continue;
                }

                if (key == "MemTotal")
                {
                    memTotalKb = kilobytes;
                }
                else
                {
                    memAvailableKb = kilobytes;
                }
            }

            if (memTotalKb is not > 0 || memAvailableKb is null)
            {
                return null;
            }

            return 100.0 * (1.0 - ((double)memAvailableKb / memTotalKb.Value));
        }
        catch (IOException)
        {
            return null;
        }
    }
}
