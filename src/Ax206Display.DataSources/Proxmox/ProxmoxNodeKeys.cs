namespace Ax206Display.DataSources.Proxmox;

/// <summary>
/// Builds the render-data keys a node's stats are published under - the
/// node-level counterpart to <see cref="ProxmoxGuestKeys"/>. Parameterized by
/// hostId+node name since node names aren't guaranteed unique across
/// independent (non-clustered) Proxmox hosts, only within one; widget
/// configs still reference the resulting string as their 'dataKey' setting,
/// so treat the format itself (not any specific hostId or node name) as a
/// stable contract.
/// </summary>
public static class ProxmoxNodeKeys
{
    public static string CpuUsedPercent(string hostId, string node) => $"proxmox.{hostId}.node.{node}.cpu";

    public static string MemoryUsedPercent(string hostId, string node) => $"proxmox.{hostId}.node.{node}.mem";

    public static string UptimeDays(string hostId, string node) => $"proxmox.{hostId}.node.{node}.uptimeDays";
}
