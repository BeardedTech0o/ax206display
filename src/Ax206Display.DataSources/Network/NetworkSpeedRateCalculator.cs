namespace Ax206Display.DataSources.Network;

/// <summary>
/// Turns two point-in-time cumulative byte counters into a bytes/sec rate.
/// Counters only ever increase, except when an adapter resets (link
/// renegotiation, driver reload, counter rollover) - a negative delta is
/// treated as a reset and reported as zero rather than a nonsensical
/// negative speed.
/// </summary>
public static class NetworkSpeedRateCalculator
{
    public static double ComputeBytesPerSecond(long previousBytes, long currentBytes, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return 0;
        }

        var delta = currentBytes - previousBytes;
        return delta < 0 ? 0 : delta / elapsed.TotalSeconds;
    }
}
