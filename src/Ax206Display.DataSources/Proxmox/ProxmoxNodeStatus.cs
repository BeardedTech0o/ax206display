namespace Ax206Display.DataSources.Proxmox;

public sealed record ProxmoxNodeStatus
{
    public required string Node { get; init; }

    public required string Status { get; init; }

    public double CpuUsageFraction { get; init; }

    public long MemoryUsedBytes { get; init; }

    public long MemoryTotalBytes { get; init; }

    public long UptimeSeconds { get; init; }
}
