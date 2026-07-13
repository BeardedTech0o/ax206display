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
}
