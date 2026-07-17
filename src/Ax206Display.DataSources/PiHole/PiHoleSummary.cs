namespace Ax206Display.DataSources.PiHole;

public sealed record PiHoleSummary
{
    public long AdsBlockedToday { get; init; }

    public double AdsPercentageToday { get; init; }

    public long DnsQueriesToday { get; init; }
}
