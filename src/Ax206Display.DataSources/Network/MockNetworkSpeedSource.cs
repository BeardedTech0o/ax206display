namespace Ax206Display.DataSources.Network;

/// <summary>Fixed/injectable snapshot source used by tests.</summary>
public sealed class MockNetworkSpeedSource : INetworkSpeedSource
{
    public NetworkSpeedSnapshot Snapshot { get; set; } = new()
    {
        DownloadBytesPerSecond = 1_250_000,
        UploadBytesPerSecond = 375_000,
    };

    public NetworkSpeedSnapshot GetSnapshot() => Snapshot;
}
