using System.Globalization;

namespace Ax206Display.DataSources.Proxmox;

/// <summary>
/// Builds the render-data keys a guest's stats are published under. Unlike
/// the fixed keys in SystemStatKeys/NetworkSpeedKeys, these are parameterized
/// by hostId+vmid since the set of guests (and now hosts) is only known at
/// runtime - widget configs still reference the resulting string as their
/// 'dataKey' setting, so treat the format itself (not any specific hostId or
/// vmid) as a stable contract. hostId is the owning Proxmox
/// IntegrationConfig's Id - required (not just vmid) because a VMID is only
/// unique within one host/cluster, not across independent Proxmox hosts.
/// </summary>
public static class ProxmoxGuestKeys
{
    public static string CpuUsedPercent(string hostId, int vmId) => string.Create(CultureInfo.InvariantCulture, $"proxmox.{hostId}.guest.{vmId}.cpu");

    public static string MemoryUsedPercent(string hostId, int vmId) => string.Create(CultureInfo.InvariantCulture, $"proxmox.{hostId}.guest.{vmId}.mem");
}
