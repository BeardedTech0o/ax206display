namespace Ax206Display.DataSources.Proxmox;

/// <summary>Maps each guest's/node's CPU/memory usage onto its own render-data keys (see <see cref="ProxmoxGuestKeys"/>/<see cref="ProxmoxNodeKeys"/>).</summary>
public static class ProxmoxStatsPublisher
{
    private const double SecondsPerDay = 86_400.0;

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

    public static void PublishNodes(IReadOnlyList<ProxmoxNodeStatus> nodes, Action<string, object> publish)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(publish);

        foreach (var node in nodes)
        {
            publish(ProxmoxNodeKeys.CpuUsedPercent(node.Node), node.CpuUsageFraction * 100.0);

            var memoryUsedPercent = node.MemoryTotalBytes > 0
                ? node.MemoryUsedBytes / (double)node.MemoryTotalBytes * 100.0
                : 0.0;
            publish(ProxmoxNodeKeys.MemoryUsedPercent(node.Node), memoryUsedPercent);

            publish(ProxmoxNodeKeys.UptimeDays(node.Node), node.UptimeSeconds / SecondsPerDay);
        }
    }
}
