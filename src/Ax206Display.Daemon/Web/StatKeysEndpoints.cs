using Ax206Display.DataSources.Pbs;
using Ax206Display.DataSources.Proxmox;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ax206Display.Daemon.Web;

/// <summary>
/// The fixed system/network/Pi-hole/UniFi keys are hardcoded directly into
/// editor.html (they never change at runtime). Proxmox/PBS keys can't be:
/// which hosts/nodes/guests/datastores exist is only known once a pump
/// service has actually polled them, the same reason
/// Ax206Display.App's WidgetDesignerWindow.BuildAvailableStatKeys reads
/// ProxmoxNodeDirectory/ProxmoxGuestDirectory/PbsDatastoreDirectory instead
/// of a fixed list. This is the same idea for the web editor: one endpoint
/// it fetches on load and merges into its own static list, rather than
/// duplicating the "no dynamic keys at all" gap the web editor would
/// otherwise have relative to the Windows Designer.
/// </summary>
public static class StatKeysEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/stat-keys/dynamic", GetDynamicStatKeys);
    }

    private static IResult GetDynamicStatKeys(
        ProxmoxNodeDirectory nodeDirectory, ProxmoxGuestDirectory guestDirectory, PbsDatastoreDirectory pbsDirectory)
    {
        var keys = new List<DynamicStatKey>();

        foreach (var hostSnapshot in nodeDirectory.GetSnapshot().Values)
        {
            foreach (var node in hostSnapshot.Items)
            {
                keys.Add(new DynamicStatKey(ProxmoxNodeKeys.CpuUsedPercent(hostSnapshot.HostId, node.Node), $"{hostSnapshot.DisplayName}: {node.Node} CPU"));
                keys.Add(new DynamicStatKey(ProxmoxNodeKeys.MemoryUsedPercent(hostSnapshot.HostId, node.Node), $"{hostSnapshot.DisplayName}: {node.Node} Memory"));
                keys.Add(new DynamicStatKey(ProxmoxNodeKeys.UptimeDays(hostSnapshot.HostId, node.Node), $"{hostSnapshot.DisplayName}: {node.Node} Uptime"));
            }
        }

        foreach (var hostSnapshot in guestDirectory.GetSnapshot().Values)
        {
            foreach (var guest in hostSnapshot.Items)
            {
                keys.Add(new DynamicStatKey(ProxmoxGuestKeys.CpuUsedPercent(hostSnapshot.HostId, guest.VmId), $"{hostSnapshot.DisplayName}: {guest.Name} CPU"));
                keys.Add(new DynamicStatKey(ProxmoxGuestKeys.MemoryUsedPercent(hostSnapshot.HostId, guest.VmId), $"{hostSnapshot.DisplayName}: {guest.Name} Memory"));
            }
        }

        foreach (var hostSnapshot in pbsDirectory.GetSnapshot().Values)
        {
            foreach (var datastore in hostSnapshot.Items)
            {
                keys.Add(new DynamicStatKey(PbsStatKeys.UsedPercent(hostSnapshot.HostId, datastore.Store), $"{hostSnapshot.DisplayName}: {datastore.Store} Storage"));
            }
        }

        return Results.Ok(keys);
    }

    private sealed record DynamicStatKey(string Key, string Label);
}
