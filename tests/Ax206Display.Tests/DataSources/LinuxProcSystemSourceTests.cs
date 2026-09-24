using Ax206Display.DataSources.SystemMonitor;

namespace Ax206Display.Tests.DataSources;

public class LinuxProcSystemSourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ax206display-proc-").FullName;

    private string ProcRoot => Path.Combine(_root, "proc");

    private string SysRoot => Path.Combine(_root, "sys");

    public LinuxProcSystemSourceTests()
    {
        Directory.CreateDirectory(ProcRoot);
        Directory.CreateDirectory(SysRoot);
    }

    [Fact]
    public void GetSnapshot_FirstCall_ReportsNoCpuLoadYet()
    {
        WriteStat(user: 100, idle: 900);

        var snapshot = new LinuxProcSystemSource(ProcRoot, SysRoot).GetSnapshot();

        Assert.Null(snapshot.CpuLoadPercent);
    }

    [Fact]
    public void GetSnapshot_SecondCall_ReportsBusyShareOfTheDelta()
    {
        var source = new LinuxProcSystemSource(ProcRoot, SysRoot);
        WriteStat(user: 100, idle: 900);
        source.GetSnapshot();

        // +300 busy, +100 idle, +100 iowait (counted as idle) => 300 / 500.
        WriteStat(user: 400, idle: 1000, iowait: 100);
        var snapshot = source.GetSnapshot();

        Assert.NotNull(snapshot.CpuLoadPercent);
        Assert.Equal(60, snapshot.CpuLoadPercent!.Value, precision: 6);
    }

    [Fact]
    public void GetSnapshot_ReadsMemoryFromMemAvailable()
    {
        File.WriteAllLines(Path.Combine(ProcRoot, "meminfo"),
        [
            "MemTotal:        4000000 kB",
            "MemFree:          100000 kB",
            "MemAvailable:    3000000 kB",
        ]);

        var snapshot = new LinuxProcSystemSource(ProcRoot, SysRoot).GetSnapshot();

        Assert.Equal(25, snapshot.MemoryUsedPercent!.Value, precision: 6);
    }

    [Fact]
    public void GetSnapshot_PrefersTheCpuThermalZone()
    {
        WriteThermalZone(0, "gpu-thermal", 70000);
        WriteThermalZone(1, "cpu-thermal", 48312);

        var snapshot = new LinuxProcSystemSource(ProcRoot, SysRoot).GetSnapshot();

        Assert.Equal(48.312, snapshot.CpuTemperatureCelsius!.Value, precision: 3);
    }

    [Fact]
    public void GetSnapshot_FallsBackToFirstReadableZone()
    {
        WriteThermalZone(0, "acpitz", 41000);

        var snapshot = new LinuxProcSystemSource(ProcRoot, SysRoot).GetSnapshot();

        Assert.Equal(41, snapshot.CpuTemperatureCelsius!.Value, precision: 3);
    }

    [Fact]
    public void GetSnapshot_WithNothingReadable_ReportsNothingAndDoesNotThrow()
    {
        var snapshot = new LinuxProcSystemSource(ProcRoot, SysRoot).GetSnapshot();

        Assert.Null(snapshot.CpuLoadPercent);
        Assert.Null(snapshot.CpuTemperatureCelsius);
        Assert.Null(snapshot.MemoryUsedPercent);
        Assert.Null(snapshot.GpuLoadPercent);
    }

    private void WriteStat(long user, long idle, long iowait = 0) =>
        File.WriteAllText(Path.Combine(ProcRoot, "stat"), $"cpu  {user} 0 0 {idle} {iowait} 0 0 0 0 0\ncpu0 1 2 3 4 5 6 7 8 0 0\n");

    private void WriteThermalZone(int index, string type, int milliDegrees)
    {
        var zone = Path.Combine(SysRoot, "class", "thermal", $"thermal_zone{index}");
        Directory.CreateDirectory(zone);
        File.WriteAllText(Path.Combine(zone, "type"), type + "\n");
        File.WriteAllText(Path.Combine(zone, "temp"), milliDegrees + "\n");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
