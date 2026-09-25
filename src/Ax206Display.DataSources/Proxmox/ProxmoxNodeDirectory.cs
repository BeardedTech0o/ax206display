using Ax206Display.DataSources.Common;

namespace Ax206Display.DataSources.Proxmox;

/// <summary>
/// The last-known list of nodes per configured Proxmox host - the node-level
/// counterpart to <see cref="ProxmoxGuestDirectory"/>, which its own doc
/// comment explains in full (identical shape/reasoning, node names instead
/// of VMIDs).
/// </summary>
public sealed class ProxmoxNodeDirectory
{
    private Dictionary<string, IntegrationHostSnapshot<ProxmoxNodeStatus>> _byHost = [];

    public IReadOnlyDictionary<string, IntegrationHostSnapshot<ProxmoxNodeStatus>> GetSnapshot() => _byHost;

    public void Update(string hostId, string displayName, IReadOnlyList<ProxmoxNodeStatus> nodes)
    {
        var next = new Dictionary<string, IntegrationHostSnapshot<ProxmoxNodeStatus>>(_byHost)
        {
            [hostId] = new IntegrationHostSnapshot<ProxmoxNodeStatus>(hostId, displayName, nodes),
        };
        _byHost = next;
    }

    public void RemoveHost(string hostId)
    {
        if (!_byHost.ContainsKey(hostId))
        {
            return;
        }

        var next = new Dictionary<string, IntegrationHostSnapshot<ProxmoxNodeStatus>>(_byHost);
        next.Remove(hostId);
        _byHost = next;
    }
}
