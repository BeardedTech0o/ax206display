using Ax206Display.Protocol.Commands;
using Ax206Display.Transport.Mock;

namespace Ax206Display.Tests.Transport;

public class MockAx206TransportTests
{
    [Fact]
    public async Task GetLcdParametersAsync_ReturnsConfiguredResolution()
    {
        using var transport = new MockAx206Transport("mock-1", 480, 320);

        var parameters = await transport.GetLcdParametersAsync();

        Assert.Equal(480, parameters.Width);
        Assert.Equal(320, parameters.Height);
        Assert.True(parameters.IsMarkerValid);
    }

    [Fact]
    public async Task SetPropertyAsync_RecordsLatestValue()
    {
        using var transport = new MockAx206Transport("mock-1", 480, 320);

        await transport.SetPropertyAsync(Ax206Property.Brightness, 7);

        Assert.Equal(7, transport.Properties[Ax206Property.Brightness]);
    }

    [Fact]
    public async Task BlitAsync_RecordsCallWithPixels()
    {
        using var transport = new MockAx206Transport("mock-1", 480, 320);
        var pixels = new byte[(20 - 10) * (20 - 10) * 2];

        await transport.BlitAsync(10, 10, 20, 20, pixels);

        var call = Assert.Single(transport.BlitCalls);
        Assert.Equal(10, call.Left);
        Assert.Equal(20, call.Right);
        Assert.Equal(pixels.Length, call.Pixels.Length);
    }

    [Fact]
    public async Task BlitAsync_WrongPixelLength_Throws()
    {
        using var transport = new MockAx206Transport("mock-1", 480, 320);

        await Assert.ThrowsAsync<ArgumentException>(() => transport.BlitAsync(0, 0, 10, 10, new byte[5]));
    }
}
