namespace Ax206Display.DataSources.Proxmox;

/// <summary>Maps each guest's/node's CPU/memory usage onto its own render-data keys (see <see cref="ProxmoxGuestKeys"/>/<see cref="ProxmoxNodeKeys"/>), namespaced by which configured Proxmox host they came from.</summary>
public static class ProxmoxStatsPublisher
{
    private const double SecondsPerDay = 86_400.0;

    public static void Publish(string hostId, IReadOnlyList<ProxmoxGuestStatus> guests, Action<string, object> publish)
    {
        ArgumentNullException.ThrowIfNull(hostId);
        ArgumentNullException.ThrowIfNull(guests);
        ArgumentNullException.ThrowIfNull(publish);

        foreach (var guest in guests)
        {
            publish(ProxmoxGuestKeys.CpuUsedPercent(hostId, guest.VmId), guest.CpuUsageFraction * 100.0);

            var memoryUsedPercent = guest.MemoryTotalBytes > 0
                ? guest.MemoryUsedBytes / (double)guest.MemoryTotalBytes * 100.0
                : 0.0;
            publish(ProxmoxGuestKeys.MemoryUsedPercent(hostId, guest.VmId), memoryUsedPercent);
        }
    }

    public static void PublishNodes(string hostId, IReadOnlyList<ProxmoxNodeStatus> nodes, Action<string, object> publish)
    {
        ArgumentNullException.ThrowIfNull(hostId);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(publish);

        foreach (var node in nodes)
        {
            publish(ProxmoxNodeKeys.CpuUsedPercent(hostId, node.Node), node.CpuUsageFraction * 100.0);

            var memoryUsedPercent = node.MemoryTotalBytes > 0
                ? node.MemoryUsedBytes / (double)node.MemoryTotalBytes * 100.0
                : 0.0;
            publish(ProxmoxNodeKeys.MemoryUsedPercent(hostId, node.Node), memoryUsedPercent);

            publish(ProxmoxNodeKeys.UptimeDays(hostId, node.Node), node.UptimeSeconds / SecondsPerDay);
        }
    }
}
