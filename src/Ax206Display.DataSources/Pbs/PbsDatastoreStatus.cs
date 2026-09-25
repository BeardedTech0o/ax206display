namespace Ax206Display.DataSources.Pbs;

/// <summary>One backup datastore's capacity, from a Proxmox Backup Server host.</summary>
public sealed record PbsDatastoreStatus
{
    public required string Store { get; init; }

    public long TotalBytes { get; init; }

    public long UsedBytes { get; init; }

    public long AvailableBytes { get; init; }
}
