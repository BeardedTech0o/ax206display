namespace Ax206Display.DataSources.PiHole;

public sealed record PiHoleSummary
{
    public long AdsBlockedToday { get; init; }

    public double AdsPercentageToday { get; init; }

    public long DnsQueriesToday { get; init; }

    public long DomainsOnBlocklist { get; init; }

    public long QueriesCached { get; init; }

    public long QueriesForwarded { get; init; }

    public long UniqueDomains { get; init; }

    public long ActiveClients { get; init; }

    public long TotalClients { get; init; }
}
