using Ax206Display.Transport.Discovery;

namespace Ax206Display.Transport.Mock;

/// <summary>Returns a fixed, injectable set of mock devices instead of touching real USB hardware.</summary>
public sealed class MockAx206DeviceDiscovery : IAx206DeviceDiscovery
{
    public List<IAx206Transport> Devices { get; } = [];

    public Task<IReadOnlyList<IAx206Transport>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<IAx206Transport>>(Devices);
    }
}
