namespace Ax206Display.DataSources.Proxmox;

/// <summary>A single VM ("qemu") or container ("lxc") on a Proxmox node.</summary>
public sealed record ProxmoxGuestStatus
{
    public required string Node { get; init; }

    public required int VmId { get; init; }

    public required string Name { get; init; }

    /// <summary>"qemu" for a VM, "lxc" for a container.</summary>
    public required string Type { get; init; }

    /// <summary>"running", "stopped", etc.</summary>
    public required string Status { get; init; }

    public double CpuUsageFraction { get; init; }

    public long MemoryUsedBytes { get; init; }

    public long MemoryTotalBytes { get; init; }
}
