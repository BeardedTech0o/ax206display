namespace Ax206Display.DataSources.SystemMonitor;

/// <summary>
/// Maps a <see cref="SystemStatsSnapshot"/> onto render-data keys. Takes the
/// publish/remove operations as delegates so this stays decoupled from the
/// rendering assembly (and trivially testable): sensors that reported a value
/// are published, sensors that didn't are removed so widgets fall back to
/// their placeholder instead of showing a stale number forever.
/// </summary>
public static class SystemStatsPublisher
{
    public static void Publish(SystemStatsSnapshot snapshot, Action<string, object> publish, Action<string> remove)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(remove);

        PublishOne(SystemStatKeys.CpuLoadPercent, snapshot.CpuLoadPercent, publish, remove);
        PublishOne(SystemStatKeys.CpuTemperatureCelsius, snapshot.CpuTemperatureCelsius, publish, remove);
        PublishOne(SystemStatKeys.MemoryUsedPercent, snapshot.MemoryUsedPercent, publish, remove);
        PublishOne(SystemStatKeys.GpuLoadPercent, snapshot.GpuLoadPercent, publish, remove);
        PublishOne(SystemStatKeys.GpuTemperatureCelsius, snapshot.GpuTemperatureCelsius, publish, remove);
    }

    private static void PublishOne(string key, double? value, Action<string, object> publish, Action<string> remove)
    {
        if (value is { } number)
        {
            publish(key, number);
        }
        else
        {
            remove(key);
        }
    }
}
