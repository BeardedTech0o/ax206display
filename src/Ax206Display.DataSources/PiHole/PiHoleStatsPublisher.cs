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
    }
}
