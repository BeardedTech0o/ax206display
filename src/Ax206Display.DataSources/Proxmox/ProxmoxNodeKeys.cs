namespace Ax206Display.DataSources.Proxmox;

/// <summary>
/// Builds the render-data keys a node's stats are published under - the
/// node-level counterpart to <see cref="ProxmoxGuestKeys"/>. Parameterized
/// by node name since the set of nodes is only known at runtime; widget
/// configs still reference the resulting string as their 'dataKey' setting,
/// so treat the format itself (not any specific node name) as a stable
/// contract.
/// </summary>
public static class ProxmoxNodeKeys
{
    public static string CpuUsedPercent(string node) => $"proxmox.node.{node}.cpu";

    public static string MemoryUsedPercent(string node) => $"proxmox.node.{node}.mem";

    public static string UptimeDays(string node) => $"proxmox.node.{node}.uptimeDays";
}
