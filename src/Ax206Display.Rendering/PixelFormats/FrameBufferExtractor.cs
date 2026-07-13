using SkiaSharp;

namespace Ax206Display.Rendering.PixelFormats;

/// <summary>
/// Extracts the raw RGB565 pixel bytes the AX206 protocol layer uploads to the
/// device. The AX206 wire format is big-endian per pixel (high byte first),
/// while Skia's native <see cref="SKColorType.Rgb565"/> layout is little-endian
/// on every little-endian CPU this app targets - see docs/protocol-spec.md
/// section 3 for the reverse-engineering citations. Callers writing to the
/// real device should pass <c>swapBytes: true</c>; tests that just want to
/// inspect Skia's native pixel layout can pass <c>false</c>.
/// </summary>
public static class FrameBufferExtractor
{
    public static byte[] ToRgb565Bytes(SKBitmap frame, bool swapBytes)
    {
        if (frame.ColorType != SKColorType.Rgb565)
        {
            throw new ArgumentException($"Expected an {SKColorType.Rgb565} bitmap, got {frame.ColorType}.", nameof(frame));
        }

        var pixelSpan = frame.GetPixelSpan();
        var buffer = new byte[pixelSpan.Length];
        pixelSpan.CopyTo(buffer);

        if (swapBytes)
        {
            for (var i = 0; i + 1 < buffer.Length; i += 2)
            {
                (buffer[i], buffer[i + 1]) = (buffer[i + 1], buffer[i]);
            }
        }

        return buffer;
    }
}
