using Ax206Display.DataSources.SystemMonitor;
using Ax206Display.Rendering.Playback;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ax206Display.App.Services;

/// <summary>
/// Polls the system-monitor source on a fixed interval and publishes the
/// readings into the <see cref="RenderDataHub"/> for stat widgets to render.
/// Poll failures are logged and skipped - one bad sensor read must never take
/// down the host.
/// </summary>
public sealed partial class SystemMonitorPumpService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ISystemMonitorSource _source;
    private readonly RenderDataHub _hub;
    private readonly ILogger<SystemMonitorPumpService> _logger;

    public SystemMonitorPumpService(ISystemMonitorSource source, RenderDataHub hub, ILogger<SystemMonitorPumpService> logger)
    {
        _source = source;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hop off the host's startup path immediately - sensor polling is
        // synchronous and the first read can be slow.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = _source.GetSnapshot();
                SystemStatsPublisher.Publish(snapshot, _hub.Publish, _hub.Remove);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "System monitor poll failed; keeping previous readings.")]
    private partial void LogPollFailed(Exception exception);
}
