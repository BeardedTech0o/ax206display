using System.Collections.Concurrent;

namespace Ax206Display.Transport.LibUsb;

/// <summary>
/// Remembers which USB serial numbers have ever been seen colliding across
/// two physical AX206 panels, for the lifetime of the (singleton) discovery
/// instance that owns this tracker.
///
/// Without this memory, a reconnect scan that catches only one of a
/// colliding pair - e.g. one panel dropping out mid-USB-glitch while its
/// sibling stays enumerated, or the two re-enumerating a beat apart after
/// both drop - would see that lone device as a single unambiguous serial and
/// hand it back its bare, undisambiguated device ID. That bare ID matches
/// neither supervisor's "@location"-suffixed device ID, so the display sits
/// unmatched (retried every reconnect interval) until the other panel
/// happens to enumerate in the same scan too.
/// </summary>
public sealed class AmbiguousSerialTracker
{
    private readonly ConcurrentDictionary<string, byte> _knownAmbiguousSerialNumbers = new();

    /// <summary>
    /// Reports whether <paramref name="serialNumber"/> should be
    /// disambiguated (given its own device ID an "@location" suffix) in the
    /// current scan: true if it collides with another device in this same
    /// scan (<paramref name="countInThisScan"/> &gt; 1), or if it was seen
    /// colliding in any earlier scan. Every call with a colliding count
    /// updates the memory before returning.
    /// </summary>
    public bool ShouldDisambiguate(string serialNumber, int countInThisScan)
    {
        if (countInThisScan > 1)
        {
            _knownAmbiguousSerialNumbers.TryAdd(serialNumber, 0);
            return true;
        }

        return _knownAmbiguousSerialNumbers.ContainsKey(serialNumber);
    }
}
