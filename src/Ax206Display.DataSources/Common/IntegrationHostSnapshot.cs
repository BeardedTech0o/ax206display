namespace Ax206Display.DataSources.Common;

/// <summary>
/// One configured host's last-known items (Proxmox nodes/guests, PBS
/// datastores, ...) for an integration kind that allows more than one
/// instance - tagged with enough to display it in a multi-host list. See
/// Proxmox's ProxmoxGuestDirectory/ProxmoxNodeDirectory and PBS's
/// PbsDatastoreDirectory.
/// </summary>
public sealed record IntegrationHostSnapshot<T>(string HostId, string DisplayName, IReadOnlyList<T> Items);
