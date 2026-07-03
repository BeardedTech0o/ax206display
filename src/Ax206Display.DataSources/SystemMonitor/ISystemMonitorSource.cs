namespace Ax206Display.DataSources.SystemMonitor;

public interface ISystemMonitorSource
{
    SystemStatsSnapshot GetSnapshot();
}
