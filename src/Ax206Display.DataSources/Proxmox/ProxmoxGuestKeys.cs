using System.Globalization;

namespace Ax206Display.DataSources.Proxmox;

/// <summary>
/// Builds the render-data keys a guest's stats are published under. Unlike
/// the fixed keys in SystemStatKeys/NetworkSpeedKeys, these are parameterized
/// by vmid since the set of guests is only known at runtime - widget configs
/// still reference the resulting string as their 'dataKey' setting, so
/// treat the format itself (not any specific vmid) as a stable contract.
/// </summary>
public static class ProxmoxGuestKeys
{
    public static string CpuUsedPercent(int vmId) => string.Create(CultureInfo.InvariantCulture, $"proxmox.guest.{vmId}.cpu");

    public static string MemoryUsedPercent(int vmId) => string.Create(CultureInfo.InvariantCulture, $"proxmox.guest.{vmId}.mem");
}
