namespace Ax206Display.DataSources.Network;

public interface INetworkSpeedSource
{
    NetworkSpeedSnapshot GetSnapshot();
}
