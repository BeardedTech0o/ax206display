using Ax206Display.Protocol.Commands;

namespace Ax206Display.Tests.Protocol;

public class LcdParametersResponseTests
{
    [Fact]
    public void Parse_ReadsWidthHeightAndMarker()
    {
        var bytes = new byte[] { 0xE0, 0x01, 0x40, 0x01, 0xFF }; // 480x320, valid marker

        var response = LcdParametersResponse.Parse(bytes);

        Assert.Equal(480, response.Width);
        Assert.Equal(320, response.Height);
        Assert.True(response.IsMarkerValid);
        Assert.True(response.HasPlausibleDimensions);
    }

    [Fact]
    public void Parse_InvalidMarker_IsFlaggedButStillParses()
    {
        var bytes = new byte[] { 0xE0, 0x01, 0x40, 0x01, 0x00 };

        var response = LcdParametersResponse.Parse(bytes);

        Assert.False(response.IsMarkerValid);
        Assert.True(response.HasPlausibleDimensions);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(5000, 100)]
    public void HasPlausibleDimensions_RejectsOutOfRangeValues(ushort width, ushort height)
    {
        var response = new LcdParametersResponse(width, height, IsMarkerValid: true);

        Assert.False(response.HasPlausibleDimensions);
    }

    [Fact]
    public void Parse_TooShort_Throws()
    {
        Assert.Throws<ArgumentException>(() => LcdParametersResponse.Parse(new byte[3]));
    }
}
