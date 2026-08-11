namespace Ax206Display.DataSources.Proxmox;

/// <summary>
/// The last-known list of Proxmox guests, updated by whatever polls the
/// Proxmox API and read by the Widget Designer to populate its "Reading"
/// dropdown - the Designer itself never talks to the network directly, it
/// only reads shared in-memory state (same pattern as RenderDataHub).
/// Copy-on-write: readers get an immutable snapshot with no lock needed.
/// </summary>
public sealed class ProxmoxGuestDirectory
{
    private IReadOnlyList<ProxmoxGuestStatus> _guests = [];

    public IReadOnlyList<ProxmoxGuestStatus> GetSnapshot() => _guests;

    public void Update(IReadOnlyList<ProxmoxGuestStatus> guests)
    {
        _guests = guests;
    }
}
