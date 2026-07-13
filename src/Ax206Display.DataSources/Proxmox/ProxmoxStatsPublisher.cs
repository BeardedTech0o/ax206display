namespace Ax206Display.DataSources.Proxmox;

/// <summary>Maps each guest's CPU/memory usage onto its own render-data keys (see <see cref="ProxmoxGuestKeys"/>).</summary>
public static class ProxmoxStatsPublisher
{
    public static void Publish(IReadOnlyList<ProxmoxGuestStatus> guests, Action<string, object> publish)
    {
        ArgumentNullException.ThrowIfNull(guests);
        ArgumentNullException.ThrowIfNull(publish);

        foreach (var guest in guests)
        {
            publish(ProxmoxGuestKeys.CpuUsedPercent(guest.VmId), guest.CpuUsageFraction * 100.0);

            var memoryUsedPercent = guest.MemoryTotalBytes > 0
                ? guest.MemoryUsedBytes / (double)guest.MemoryTotalBytes * 100.0
                : 0.0;
            publish(ProxmoxGuestKeys.MemoryUsedPercent(guest.VmId), memoryUsedPercent);
        }
    }
}
