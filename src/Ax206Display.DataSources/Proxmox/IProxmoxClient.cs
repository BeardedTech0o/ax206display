namespace Ax206Display.DataSources.Proxmox;

public interface IProxmoxClient
{
    Task LoginAsync(string username, string password, string realm = "pam", CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProxmoxNodeStatus>> GetNodeStatusesAsync(CancellationToken cancellationToken = default);

    /// <summary>Every VM and container across every node in the cluster.</summary>
    Task<IReadOnlyList<ProxmoxGuestStatus>> GetGuestStatusesAsync(CancellationToken cancellationToken = default);
}
