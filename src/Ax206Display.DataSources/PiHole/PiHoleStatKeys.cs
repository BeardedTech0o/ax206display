namespace Ax206Display.DataSources.PiHole;

/// <summary>
/// The render-data keys the Pi-hole pump publishes <see cref="PiHoleSummary"/>
/// values under. Widget configs reference these as their 'dataKey' setting -
/// treat them as a public contract, renaming one breaks existing saved layouts.
/// </summary>
public static class PiHoleStatKeys
{
    public const string AdsBlockedToday = "pihole.adsBlockedToday";
    public const string AdsPercentageToday = "pihole.adsPercentageToday";
    public const string DnsQueriesToday = "pihole.dnsQueriesToday";
}
