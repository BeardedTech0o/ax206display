using System.Net.NetworkInformation;

namespace Ax206Display.DataSources.Network;

/// <summary>
/// Sums bytes sent/received across all "real" (up, non-loopback, non-tunnel)
/// network interfaces and reports the rate since the previous call. The
/// first call after construction always returns nulls - there's no prior
/// sample yet to compute a rate from.
/// </summary>
public sealed class NetworkInterfaceSpeedSource : INetworkSpeedSource
{
    private long? _lastBytesReceived;
    private long? _lastBytesSent;
    private DateTimeOffset _lastSampleTime;

    public NetworkSpeedSnapshot GetSnapshot()
    {
        var (bytesReceived, bytesSent) = ReadTotalBytes();
        var now = DateTimeOffset.UtcNow;

        if (_lastBytesReceived is not { } previousReceived || _lastBytesSent is not { } previousSent)
        {
            _lastBytesReceived = bytesReceived;
            _lastBytesSent = bytesSent;
            _lastSampleTime = now;
            return new NetworkSpeedSnapshot();
        }

        var elapsed = now - _lastSampleTime;
        var snapshot = new NetworkSpeedSnapshot
        {
            DownloadBytesPerSecond = NetworkSpeedRateCalculator.ComputeBytesPerSecond(previousReceived, bytesReceived, elapsed),
            UploadBytesPerSecond = NetworkSpeedRateCalculator.ComputeBytesPerSecond(previousSent, bytesSent, elapsed),
        };

        _lastBytesReceived = bytesReceived;
        _lastBytesSent = bytesSent;
        _lastSampleTime = now;

        return snapshot;
    }

    private static (long BytesReceived, long BytesSent) ReadTotalBytes()
    {
        long received = 0;
        long sent = 0;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var stats = nic.GetIPv4Statistics();
            received += stats.BytesReceived;
            sent += stats.BytesSent;
        }

        return (received, sent);
    }
}
