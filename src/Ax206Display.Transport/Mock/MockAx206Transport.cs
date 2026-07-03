using Ax206Display.Protocol.Commands;

namespace Ax206Display.Transport.Mock;

/// <summary>
/// A fake AX206 device for tests and the widget designer's live preview. It
/// mirrors what a real display would report/store, without any USB I/O, so
/// rendering, config, and data-source code can be exercised without hardware.
/// </summary>
public sealed class MockAx206Transport : IAx206Transport
{
    private readonly Dictionary<Ax206Property, ushort> _properties = new();

    public MockAx206Transport(string deviceId, ushort width, ushort height)
    {
        DeviceId = deviceId;
        Width = width;
        Height = height;
    }

    public string DeviceId { get; }

    public ushort Width { get; }

    public ushort Height { get; }

    /// <summary>Every blit call this mock has received, in order, for assertions in tests.</summary>
    public List<BlitCall> BlitCalls { get; } = [];

    public IReadOnlyDictionary<Ax206Property, ushort> Properties => _properties;

    public Task<LcdParametersResponse> GetLcdParametersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LcdParametersResponse(Width, Height, IsMarkerValid: true));
    }

    public Task SetPropertyAsync(Ax206Property property, ushort value, CancellationToken cancellationToken = default)
    {
        _properties[property] = value;
        return Task.CompletedTask;
    }

    public Task BlitAsync(ushort left, ushort top, ushort right, ushort bottom, ReadOnlyMemory<byte> rgb565BigEndianPixels, CancellationToken cancellationToken = default)
    {
        var expectedLength = (right - left) * (bottom - top) * 2;
        if (rgb565BigEndianPixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Expected {expectedLength} bytes for a {right - left}x{bottom - top} RGB565 rectangle, got {rgb565BigEndianPixels.Length}.");
        }

        BlitCalls.Add(new BlitCall(left, top, right, bottom, rgb565BigEndianPixels.ToArray()));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    public sealed record BlitCall(ushort Left, ushort Top, ushort Right, ushort Bottom, byte[] Pixels);
}
