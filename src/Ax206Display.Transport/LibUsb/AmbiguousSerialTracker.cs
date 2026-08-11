using System.Collections.Concurrent;

namespace Ax206Display.Transport.LibUsb;

/// <summary>
/// Remembers which USB serial numbers have ever been seen colliding across
/// two physical AX206 panels, for the lifetime of the (singleton) discovery
/// instance that owns this tracker - plus whatever a caller seeds in from
/// outside that (see <see cref="Seed"/>), which is how this memory survives
/// a process restart even though the tracker itself does not.
///
/// Without this memory, a reconnect scan that catches only one of a
/// colliding pair - e.g. one panel dropping out mid-USB-glitch while its
/// sibling stays enumerated, or the two re-enumerating a beat apart after
/// both drop - would see that lone device as a single unambiguous serial and
/// hand it back its bare, undisambiguated device ID. That bare ID matches
/// neither supervisor's "@location"-suffixed device ID, so the display sits
/// unmatched (retried every reconnect interval) until the other panel
/// happens to enumerate in the same scan too. The same failure mode also
/// happens on a full app restart, and is worse there: if only one panel of
/// the pair has finished USB enumeration by the time the very first
/// post-restart scan runs (common at boot, when hubs power up a beat apart),
/// this tracker starts out empty and has no earlier scan to remember, so the
/// lone panel is misread as a brand-new display and gets a fresh
/// auto-provisioned default layout instead of reattaching to its saved one.
/// </summary>
public sealed class AmbiguousSerialTracker
{
    private readonly ConcurrentDictionary<string, byte> _knownAmbiguousSerialNumbers = new();

    /// <summary>
    /// Reports whether <paramref name="serialNumber"/> should be
    /// disambiguated (given its own device ID an "@location" suffix) in the
    /// current scan: true if it collides with another device in this same
    /// scan (<paramref name="countInThisScan"/> &gt; 1), or if it was seen
    /// colliding in any earlier scan (including one seeded via
    /// <see cref="Seed"/>). Every call with a colliding count updates the
    /// memory before returning.
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

    /// <summary>
    /// Primes the collision memory with serial numbers already known - from
    /// a source outside this scan/process, typically saved config profile
    /// IDs left over from a previous run's disambiguation - to belong to a
    /// colliding pair, so the very first scan after a restart disambiguates
    /// them even if it only enumerates one of the two panels.
    /// </summary>
    public void Seed(IEnumerable<string> serialNumbers)
    {
        foreach (var serialNumber in serialNumbers)
        {
            _knownAmbiguousSerialNumbers.TryAdd(serialNumber, 0);
        }
    }
}
