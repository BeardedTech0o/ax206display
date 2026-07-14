namespace Ax206Display.DataSources.UniFi;

/// <summary>
/// NumUser (connected client count) is populated on the "lan"/"wlan"
/// subsystems; RxBytesPerSecond/TxBytesPerSecond (WAN throughput) on "wan" -
/// see docs on UniFiStatsPublisher for how these map onto widget stats.
/// </summary>
public sealed record UniFiSubsystemHealth(string Subsystem, string Status, int NumUser = 0, double RxBytesPerSecond = 0, double TxBytesPerSecond = 0);

public sealed record UniFiSiteStatus(IReadOnlyList<UniFiSubsystemHealth> Subsystems);
