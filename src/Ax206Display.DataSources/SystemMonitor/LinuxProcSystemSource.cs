using System.Globalization;

namespace Ax206Display.DataSources.SystemMonitor;

/// <summary>
/// Reads CPU load, CPU temperature and memory use straight from procfs/sysfs,
/// for Linux hosts (a Raspberry Pi being the main target) where
/// LibreHardwareMonitorLib has nothing to offer. Needs no elevation: every
/// file read here is world-readable on a stock kernel. GPU load/temperature
/// have no portable kernel interface, so they're always reported as missing
/// and render as the widget's placeholder.
/// </summary>
public sealed class LinuxProcSystemSource : ISystemMonitorSource
{
    private readonly string _procRoot;
    private readonly string _sysRoot;
    private readonly object _sync = new();
    private CpuTimes? _previousCpuTimes;

    /// <param name="procRoot">Mount point of procfs; overridable so tests can point at fixture files.</param>
    /// <param name="sysRoot">Mount point of sysfs; overridable for the same reason.</param>
    public LinuxProcSystemSource(string procRoot = "/proc", string sysRoot = "/sys")
    {
        _procRoot = procRoot;
        _sysRoot = sysRoot;
    }

    public SystemStatsSnapshot GetSnapshot()
    {
        return new SystemStatsSnapshot
        {
            CpuLoadPercent = ReadCpuLoadPercent(),
            CpuTemperatureCelsius = ReadCpuTemperatureCelsius(),
            MemoryUsedPercent = ReadMemoryUsedPercent(),
        };
    }

    /// <summary>
    /// /proc/stat counters are cumulative since boot, so load is the busy
    /// share of the delta between two reads. The very first call has no
    /// previous sample to diff against and reports nothing rather than a
    /// since-boot average that would read as a misleadingly flat number.
    /// </summary>
    private double? ReadCpuLoadPercent()
    {
        var current = TryReadCpuTimes();
        if (current is null)
        {
            return null;
        }

        CpuTimes? previous;
        lock (_sync)
        {
            previous = _previousCpuTimes;
            _previousCpuTimes = current;
        }

        if (previous is null)
        {
            return null;
        }

        var totalDelta = current.Total - previous.Total;
        var idleDelta = current.Idle - previous.Idle;
        if (totalDelta <= 0)
        {
            return null;
        }

        return Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
    }

    private CpuTimes? TryReadCpuTimes()
    {
        var firstLine = TryReadFirstLine(Path.Combine(_procRoot, "stat"));
        if (firstLine is null || !firstLine.StartsWith("cpu ", StringComparison.Ordinal))
        {
            return null;
        }

        // cpu user nice system idle iowait irq softirq steal guest guest_nice
        // guest/guest_nice are already counted inside user/nice, so only the
        // first eight fields make up the total.
        var fields = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var values = new List<long>();
        foreach (var field in fields.Skip(1).Take(8))
        {
            if (!long.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            values.Add(value);
        }

        if (values.Count < 4)
        {
            return null;
        }

        var idle = values[3] + (values.Count > 4 ? values[4] : 0);
        return new CpuTimes(values.Sum(), idle);
    }

    /// <summary>
    /// Picks the thermal zone that best represents the CPU: a zone whose type
    /// names the CPU/SoC (a Pi reports "cpu-thermal", x86 "x86_pkg_temp"),
    /// else the first readable zone. Values are in millidegrees Celsius.
    /// </summary>
    private double? ReadCpuTemperatureCelsius()
    {
        var thermalRoot = Path.Combine(_sysRoot, "class", "thermal");
        if (!Directory.Exists(thermalRoot))
        {
            return null;
        }

        double? fallback = null;
        foreach (var zone in Directory.EnumerateDirectories(thermalRoot, "thermal_zone*").Order(StringComparer.Ordinal))
        {
            var milliDegrees = TryReadFirstLine(Path.Combine(zone, "temp"));
            if (!long.TryParse(milliDegrees, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var celsius = value / 1000.0;
            var type = TryReadFirstLine(Path.Combine(zone, "type")) ?? string.Empty;
            if (type.Contains("cpu", StringComparison.OrdinalIgnoreCase)
                || type.Contains("x86_pkg", StringComparison.OrdinalIgnoreCase)
                || type.Contains("soc", StringComparison.OrdinalIgnoreCase))
            {
                return celsius;
            }

            fallback ??= celsius;
        }

        return fallback;
    }

    private double? ReadMemoryUsedPercent()
    {
        long? totalKb = null;
        long? availableKb = null;

        try
        {
            foreach (var line in File.ReadLines(Path.Combine(_procRoot, "meminfo")))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    totalKb = ParseMeminfoKb(line);
                }
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                {
                    availableKb = ParseMeminfoKb(line);
                }

                if (totalKb is not null && availableKb is not null)
                {
                    break;
                }
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (totalKb is not > 0 || availableKb is null)
        {
            return null;
        }

        return Math.Clamp(100.0 * (totalKb.Value - availableKb.Value) / totalKb.Value, 0, 100);
    }

    private static long? ParseMeminfoKb(string line)
    {
        // "MemTotal:        3884328 kB"
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? TryReadFirstLine(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            return reader.ReadLine()?.Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record CpuTimes(long Total, long Idle);
}
