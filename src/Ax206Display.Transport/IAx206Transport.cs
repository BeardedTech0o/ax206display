using Ax206Display.Protocol.Commands;

namespace Ax206Display.Transport;

/// <summary>
/// The single seam all AX206 USB I/O goes through. Every operation the app
/// needs from a physical (or mocked) display is exposed here so
/// rendering/config/data-source code never depends on a concrete USB backend
/// and can be exercised in tests via <see cref="Mock.MockAx206Transport"/>.
/// </summary>
public interface IAx206Transport : IDisposable
{
    /// <summary>A stable string identifying the underlying device (serial number or USB location path).</summary>
    string DeviceId { get; }

    Task<LcdParametersResponse> GetLcdParametersAsync(CancellationToken cancellationToken = default);

    Task SetPropertyAsync(Ax206Property property, ushort value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a rectangle of pixel data. <paramref name="rgb565BigEndianPixels"/>
    /// must already be big-endian RGB565 (see Ax206Display.Rendering's
    /// FrameBufferExtractor, which the render pipeline uses to produce it) and
    /// exactly <c>(right-left)*(bottom-top)*2</c> bytes long.
    /// </summary>
    Task BlitAsync(ushort left, ushort top, ushort right, ushort bottom, ReadOnlyMemory<byte> rgb565BigEndianPixels, CancellationToken cancellationToken = default);
}
