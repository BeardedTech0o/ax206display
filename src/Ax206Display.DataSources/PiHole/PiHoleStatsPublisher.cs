namespace Ax206Display.DataSources.PiHole;

public static class PiHoleStatsPublisher
{
    public static void Publish(PiHoleSummary summary, Action<string, object> publish)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(publish);

        publish(PiHoleStatKeys.AdsBlockedToday, (double)summary.AdsBlockedToday);
        publish(PiHoleStatKeys.AdsPercentageToday, summary.AdsPercentageToday);
        publish(PiHoleStatKeys.DnsQueriesToday, (double)summary.DnsQueriesToday);
        publish(PiHoleStatKeys.DomainsOnBlocklist, (double)summary.DomainsOnBlocklist);
        publish(PiHoleStatKeys.QueriesCached, (double)summary.QueriesCached);
        publish(PiHoleStatKeys.QueriesForwarded, (double)summary.QueriesForwarded);
        publish(PiHoleStatKeys.UniqueDomains, (double)summary.UniqueDomains);
        publish(PiHoleStatKeys.ActiveClients, (double)summary.ActiveClients);
        publish(PiHoleStatKeys.TotalClients, (double)summary.TotalClients);
    }
}
