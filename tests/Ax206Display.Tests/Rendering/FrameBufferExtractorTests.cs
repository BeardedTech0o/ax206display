using Ax206Display.Rendering.PixelFormats;
using SkiaSharp;

namespace Ax206Display.Tests.Rendering;

public class FrameBufferExtractorTests
{
    [Fact]
    public void ToRgb565Bytes_WithoutSwap_MatchesSkiaNativeLayout()
    {
        using var bitmap = MakeSinglePixelBitmap();

        var bytes = FrameBufferExtractor.ToRgb565Bytes(bitmap, swapBytes: false);

        Assert.Equal(2, bytes.Length);
        Assert.Equal(bitmap.GetPixelSpan().ToArray(), bytes);
    }

    [Fact]
    public void ToRgb565Bytes_WithSwap_ReversesEachPixelsTwoBytes()
    {
        using var bitmap = MakeSinglePixelBitmap();
        var nativeBytes = bitmap.GetPixelSpan().ToArray();

        var swapped = FrameBufferExtractor.ToRgb565Bytes(bitmap, swapBytes: true);

        Assert.Equal(nativeBytes[0], swapped[1]);
        Assert.Equal(nativeBytes[1], swapped[0]);
    }

    [Fact]
    public void ToRgb565Bytes_WrongColorType_Throws()
    {
        using var bitmap = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul);

        Assert.Throws<ArgumentException>(() => FrameBufferExtractor.ToRgb565Bytes(bitmap, swapBytes: false));
    }

    private static SKBitmap MakeSinglePixelBitmap()
    {
        var bitmap = new SKBitmap(1, 1, SKColorType.Rgb565, SKAlphaType.Opaque);
        bitmap.SetPixel(0, 0, new SKColor(0x12, 0x34, 0x56));
        return bitmap;
    }
}
