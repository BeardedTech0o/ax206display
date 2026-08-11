namespace Ax206Display.DataSources.Network;

public sealed record NetworkSpeedSnapshot
{
    public double? DownloadBytesPerSecond { get; init; }

    public double? UploadBytesPerSecond { get; init; }
}
