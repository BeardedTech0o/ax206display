using Ax206Display.Rendering.Playback;

namespace Ax206Display.Tests.Rendering;

public class RenderDataHubTests
{
    [Fact]
    public void GetSnapshot_ReflectsPublishedValues()
    {
        var hub = new RenderDataHub();

        hub.Publish("a", 1.0);
        hub.Publish("b", "text");

        var snapshot = hub.GetSnapshot();
        Assert.Equal(1.0, snapshot["a"]);
        Assert.Equal("text", snapshot["b"]);
    }

    [Fact]
    public void Publish_DoesNotMutateEarlierSnapshots()
    {
        var hub = new RenderDataHub();
        hub.Publish("a", 1.0);

        var before = hub.GetSnapshot();
        hub.Publish("a", 2.0);

        Assert.Equal(1.0, before["a"]);
        Assert.Equal(2.0, hub.GetSnapshot()["a"]);
    }

    [Fact]
    public void Remove_DropsTheKey()
    {
        var hub = new RenderDataHub();
        hub.Publish("a", 1.0);

        hub.Remove("a");

        Assert.False(hub.GetSnapshot().ContainsKey("a"));
    }

    [Fact]
    public void Remove_MissingKey_IsANoOp()
    {
        var hub = new RenderDataHub();
        hub.Publish("a", 1.0);

        hub.Remove("never-published");

        Assert.Equal(1.0, hub.GetSnapshot()["a"]);
    }

    [Fact]
    public void GetHistory_ReturnsEveryPublishedNumericSampleInOrder()
    {
        var hub = new RenderDataHub();

        hub.Publish("a", 1.0);
        hub.Publish("a", 2.0);
        hub.Publish("a", 3.0);

        var history = hub.GetHistory("a", TimeSpan.FromDays(1));

        Assert.Equal([1.0, 2.0, 3.0], history.Select(s => s.Value));
    }

    [Fact]
    public void GetHistory_ForNeverPublishedKey_ReturnsEmptyInsteadOfThrowing()
    {
        var hub = new RenderDataHub();

        Assert.Empty(hub.GetHistory("never-published", TimeSpan.FromDays(1)));
    }

    [Fact]
    public void GetHistory_IgnoresNonNumericValues()
    {
        var hub = new RenderDataHub();

        hub.Publish("a", "not a number");

        Assert.Empty(hub.GetHistory("a", TimeSpan.FromDays(1)));
    }

    [Fact]
    public void GetHistory_OutsideTheWindow_IsExcluded()
    {
        var hub = new RenderDataHub();
        hub.Publish("a", 1.0);

        // A zero-width window should exclude the sample recorded just before
        // this call (its timestamp is strictly earlier than "now" at the
        // moment GetHistory computes its own cutoff).
        Thread.Sleep(5);
        var history = hub.GetHistory("a", TimeSpan.Zero);

        Assert.Empty(history);
    }

    [Fact]
    public void Remove_DoesNotErasePastHistory()
    {
        // A transient sensor outage should leave a chart's past data intact -
        // Remove only affects the latest-snapshot view, not history.
        var hub = new RenderDataHub();
        hub.Publish("a", 1.0);

        hub.Remove("a");

        Assert.Single(hub.GetHistory("a", TimeSpan.FromDays(1)));
    }
}
