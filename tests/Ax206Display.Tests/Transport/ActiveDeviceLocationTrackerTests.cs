using Ax206Display.Transport.LibUsb;

namespace Ax206Display.Tests.Transport;

public class ActiveDeviceLocationTrackerTests
{
    [Fact]
    public void IsKnownLocationOf_TrueWhenLocationMatchesARememberedDeviceId()
    {
        var tracker = new ActiveDeviceLocationTracker<int>();
        tracker.Remember("R-display", location: 7);

        Assert.True(tracker.IsKnownLocationOf(7, ["R-display"]));
    }

    [Fact]
    public void IsKnownLocationOf_FalseForALocationNeverRemembered()
    {
        var tracker = new ActiveDeviceLocationTracker<int>();
        tracker.Remember("R-display", location: 7);

        Assert.False(tracker.IsKnownLocationOf(8, ["R-display"]));
    }

    [Fact]
    public void IsKnownLocationOf_FalseWhenTheMatchingDeviceIdIsNotInTheQueriedSet()
    {
        // Simulates the reconnect case this class exists for: M is being
        // searched for, so its own last-known location must not read as
        // "already active" just because it happens to still be in the
        // tracker's memory from before it disconnected.
        var tracker = new ActiveDeviceLocationTracker<int>();
        tracker.Remember("M-display", location: 7);

        Assert.False(tracker.IsKnownLocationOf(7, ["R-display", "L-display"]));
    }

    [Fact]
    public void Remember_LatestLocationWinsWhenADeviceMoves()
    {
        var tracker = new ActiveDeviceLocationTracker<int>();
        tracker.Remember("R-display", location: 7);
        tracker.Remember("R-display", location: 9);

        Assert.False(tracker.IsKnownLocationOf(7, ["R-display"]));
        Assert.True(tracker.IsKnownLocationOf(9, ["R-display"]));
    }

    [Fact]
    public void IsKnownLocationOf_FalseWhenNoDeviceIdsAreQueried()
    {
        var tracker = new ActiveDeviceLocationTracker<int>();
        tracker.Remember("R-display", location: 7);

        Assert.False(tracker.IsKnownLocationOf(7, []));
    }
}
