namespace Ax206Display.DataSources.Proxmox;

/// <summary>
/// The last-known list of Proxmox nodes, updated by whatever polls the
/// Proxmox API and read by the Widget Designer to populate its "Reading"
/// dropdown - see <see cref="ProxmoxGuestDirectory"/> for the identical
/// pattern this mirrors at the node (rather than guest) level.
/// Copy-on-write: readers get an immutable snapshot with no lock needed.
/// </summary>
public sealed class ProxmoxNodeDirectory
{
    private IReadOnlyList<ProxmoxNodeStatus> _nodes = [];

    public IReadOnlyList<ProxmoxNodeStatus> GetSnapshot() => _nodes;

    public void Update(IReadOnlyList<ProxmoxNodeStatus> nodes)
    {
        _nodes = nodes;
    }
}
