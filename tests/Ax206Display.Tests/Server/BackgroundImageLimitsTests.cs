using Ax206Display.Server.Api;
using SkiaSharp;

namespace Ax206Display.Tests.Server;

public class BackgroundImageLimitsTests
{
    [Theory]
    [InlineData(480, 320)]
    [InlineData(4032, 3024)]   // 12 MP phone photo
    [InlineData(8000, 6000)]   // 48 MP phone photo
    [InlineData(1, 1)]
    public void IsAcceptableImageSize_AcceptsRealisticImages(int width, int height)
    {
        Assert.True(DeviceEndpoints.IsAcceptableImageSize(width, height));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(30000, 30000)]  // decompression-bomb territory
    [InlineData(16385, 10)]
    [InlineData(10, 16385)]
    [InlineData(10000, 10000)]  // 100 MP
    public void IsAcceptableImageSize_RejectsAbsurdOrInvalidSizes(int width, int height)
    {
        Assert.False(DeviceEndpoints.IsAcceptableImageSize(width, height));
    }

    [Fact]
    public void SkCodec_ReportsDimensionsFromTheHeaderWithoutADecode()
    {
        // The upload path relies on this: dimensions are available from the
        // header, so an oversized image can be refused before any pixel buffer
        // is allocated.
        using var bitmap = new SKBitmap(64, 32);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        using var data = SKData.CreateCopy(encoded.ToArray());
        using var codec = SKCodec.Create(data);

        Assert.NotNull(codec);
        Assert.Equal(64, codec!.Info.Width);
        Assert.Equal(32, codec.Info.Height);
    }
}
