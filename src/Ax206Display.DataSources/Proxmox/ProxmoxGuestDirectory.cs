namespace Ax206Display.DataSources.Proxmox;

/// <summary>
/// The last-known list of guests per configured Proxmox host, updated by
/// whatever polls each host's API and read by the Widget Designer to
/// populate its "Reading" dropdown - the Designer itself never talks to the
/// network directly, it only reads shared in-memory state (same pattern as
/// RenderDataHub). Keyed by host (IntegrationConfig.Id) rather than a flat
/// list because a VMID is only unique within one host/cluster, not across
/// independent Proxmox hosts - see ProxmoxGuestKeys. Copy-on-write: readers
/// get an immutable snapshot with no lock needed.
/// </summary>
public sealed class ProxmoxGuestDirectory
{
    private Dictionary<string, ProxmoxHostSnapshot<ProxmoxGuestStatus>> _byHost = [];

    public IReadOnlyDictionary<string, ProxmoxHostSnapshot<ProxmoxGuestStatus>> GetSnapshot() => _byHost;

    public void Update(string hostId, string displayName, IReadOnlyList<ProxmoxGuestStatus> guests)
    {
        var next = new Dictionary<string, ProxmoxHostSnapshot<ProxmoxGuestStatus>>(_byHost)
        {
            [hostId] = new ProxmoxHostSnapshot<ProxmoxGuestStatus>(hostId, displayName, guests),
        };
        _byHost = next;
    }

    /// <summary>Drops a host entirely, e.g. once its integration is removed from config - otherwise a removed host's last-seen guests would linger forever in the designer's dropdown.</summary>
    public void RemoveHost(string hostId)
    {
        if (!_byHost.ContainsKey(hostId))
        {
            return;
        }

        var next = new Dictionary<string, ProxmoxHostSnapshot<ProxmoxGuestStatus>>(_byHost);
        next.Remove(hostId);
        _byHost = next;
    }
}
