using Ax206Display.DataSources.Common;

namespace Ax206Display.DataSources.Pbs;

/// <summary>
/// The last-known list of datastores per configured PBS host - the PBS
/// counterpart to Proxmox's ProxmoxNodeDirectory/ProxmoxGuestDirectory
/// (identical shape/reasoning: read by the Widget Designer to populate its
/// "Reading" dropdown without touching the network itself, keyed by host
/// since a store name is only unique within one PBS host).
/// </summary>
public sealed class PbsDatastoreDirectory
{
    private Dictionary<string, IntegrationHostSnapshot<PbsDatastoreStatus>> _byHost = [];

    public IReadOnlyDictionary<string, IntegrationHostSnapshot<PbsDatastoreStatus>> GetSnapshot() => _byHost;

    public void Update(string hostId, string displayName, IReadOnlyList<PbsDatastoreStatus> datastores)
    {
        var next = new Dictionary<string, IntegrationHostSnapshot<PbsDatastoreStatus>>(_byHost)
        {
            [hostId] = new IntegrationHostSnapshot<PbsDatastoreStatus>(hostId, displayName, datastores),
        };
        _byHost = next;
    }

    public void RemoveHost(string hostId)
    {
        if (!_byHost.ContainsKey(hostId))
        {
            return;
        }

        var next = new Dictionary<string, IntegrationHostSnapshot<PbsDatastoreStatus>>(_byHost);
        next.Remove(hostId);
        _byHost = next;
    }
}
