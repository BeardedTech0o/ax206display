using Ax206Display.Transport.LibUsb;

namespace Ax206Display.Tests.Transport;

public class AmbiguousSerialTrackerTests
{
    [Fact]
    public void ShouldDisambiguate_TrueWhenSerialCollidesInThisScan()
    {
        var tracker = new AmbiguousSerialTracker();

        Assert.True(tracker.ShouldDisambiguate("20201115", countInThisScan: 2));
    }

    [Fact]
    public void ShouldDisambiguate_FalseForAUniqueSerialNeverSeenColliding()
    {
        var tracker = new AmbiguousSerialTracker();

        Assert.False(tracker.ShouldDisambiguate("20201115", countInThisScan: 1));
    }

    [Fact]
    public void ShouldDisambiguate_StaysTrueForASerialThatCollidedInAnEarlierScan()
    {
        // Simulates the reconnect scenario this class exists for: two panels
        // sharing a serial collide once (e.g. at app startup), then one of
        // them drops out and a later reconnect scan only finds the other one
        // alone. It must still be disambiguated so its device ID keeps
        // matching the supervisor that's waiting for it.
        var tracker = new AmbiguousSerialTracker();
        tracker.ShouldDisambiguate("20201115", countInThisScan: 2);

        Assert.True(tracker.ShouldDisambiguate("20201115", countInThisScan: 1));
    }

    [Fact]
    public void ShouldDisambiguate_DoesNotAffectUnrelatedSerials()
    {
        var tracker = new AmbiguousSerialTracker();
        tracker.ShouldDisambiguate("20201115", countInThisScan: 2);

        Assert.False(tracker.ShouldDisambiguate("some-other-serial", countInThisScan: 1));
    }

    [Fact]
    public void Seed_MakesASerialDisambiguateEvenWhenSeenAloneForTheFirstTime()
    {
        // Simulates a full app restart where only one panel of a
        // known-colliding pair has finished USB enumeration by the time the
        // very first scan runs - the tracker has no prior scan of its own to
        // remember the collision from, so it must be told up front instead.
        var tracker = new AmbiguousSerialTracker();
        tracker.Seed(["20201115"]);

        Assert.True(tracker.ShouldDisambiguate("20201115", countInThisScan: 1));
    }

    [Fact]
    public void Seed_DoesNotAffectUnrelatedSerials()
    {
        var tracker = new AmbiguousSerialTracker();
        tracker.Seed(["20201115"]);

        Assert.False(tracker.ShouldDisambiguate("some-other-serial", countInThisScan: 1));
    }
}
