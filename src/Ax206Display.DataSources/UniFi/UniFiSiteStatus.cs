namespace Ax206Display.DataSources.UniFi;

public sealed record UniFiSubsystemHealth(string Subsystem, string Status);

public sealed record UniFiSiteStatus(IReadOnlyList<UniFiSubsystemHealth> Subsystems);
