namespace Ax206Display.DataSources.Proxmox;

/// <summary>One configured Proxmox/PBS host's last-known items (nodes or guests), tagged with enough to display it in a multi-host list - see <see cref="ProxmoxGuestDirectory"/>/<see cref="ProxmoxNodeDirectory"/>.</summary>
public sealed record ProxmoxHostSnapshot<T>(string HostId, string DisplayName, IReadOnlyList<T> Items);
