using Ax206Display.DataSources.Network;
using Ax206Display.Rendering.Playback;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ax206Display.App.Services;

/// <summary>
/// Polls the network speed source on a fixed interval and publishes the
/// readings into the <see cref="RenderDataHub"/> for stat widgets to render.
/// Poll failures are logged and skipped - one bad read must never take down
/// the host.
/// </summary>
public sealed partial class NetworkSpeedPumpService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly INetworkSpeedSource _source;
    private readonly RenderDataHub _hub;
    private readonly ILogger<NetworkSpeedPumpService> _logger;

    public NetworkSpeedPumpService(INetworkSpeedSource source, RenderDataHub hub, ILogger<NetworkSpeedPumpService> logger)
    {
        _source = source;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hop off the host's startup path immediately - the first read
        // establishes the baseline counters and reports no rate yet.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = _source.GetSnapshot();
                NetworkSpeedPublisher.Publish(snapshot, _hub.Publish, _hub.Remove);
            }
            catch (Exception ex)
            {
                LogPollFailed(ex);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Network speed poll failed; keeping previous readings.")]
    private partial void LogPollFailed(Exception exception);
}
