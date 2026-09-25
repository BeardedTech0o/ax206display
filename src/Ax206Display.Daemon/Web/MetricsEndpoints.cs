using Ax206Display.DataSources.Network;
using Ax206Display.DataSources.Pbs;
using Ax206Display.DataSources.Proxmox;
using Ax206Display.DataSources.SystemMonitor;
using Ax206Display.Rendering.Playback;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ax206Display.Daemon.Web;

/// <summary>
/// Serves RenderDataHub's retained history (see RenderDataHub.GetHistory)
/// over HTTP for the web dashboard's charts - the same history a panel's
/// ChartWidget reads directly in-process, just exposed to the browser - plus
/// a current-values snapshot for the dashboard's non-chart bars (system/
/// Proxmox/PBS), which only needs "now", not a time series.
/// </summary>
public static class MetricsEndpoints
{
    private static readonly Dictionary<string, TimeSpan> Ranges = new()
    {
        ["1h"] = TimeSpan.FromHours(1),
        ["24h"] = TimeSpan.FromHours(24),
        ["7d"] = TimeSpan.FromDays(7),
    };

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/metrics/history", GetHistory);
        app.MapGet("/api/metrics/dashboard", GetDashboard);
    }

    private static IResult GetHistory(string key, string? range, RenderDataHub hub)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Results.BadRequest(new { detail = "A 'key' query parameter is required." });
        }

        if (!Ranges.TryGetValue(range ?? "1h", out var window))
        {
            return Results.BadRequest(new { detail = $"Unknown range '{range}'. Expected one of: {string.Join(", ", Ranges.Keys)}." });
        }

        var samples = hub.GetHistory(key, window)
            .Select(s => new { t = s.Timestamp.ToUnixTimeMilliseconds(), v = s.Value });
        return Results.Ok(samples);
    }

    private static IResult GetDashboard(
        RenderDataHub hub,
        ProxmoxNodeDirectory proxmoxNodes,
        PbsDatastoreDirectory pbsDatastores)
    {
        var snapshot = hub.GetSnapshot();

        double? Get(string key) => snapshot.TryGetValue(key, out var v) && v is double d ? d : null;

        var system = new
        {
            cpuPercent = Get(SystemStatKeys.CpuLoadPercent),
            memoryPercent = Get(SystemStatKeys.MemoryUsedPercent),
            diskPercent = Get(SystemStatKeys.DiskUsedPercent),
        };

        var network = new
        {
            downloadMbps = Get(NetworkSpeedKeys.DownloadMbps),
            uploadMbps = Get(NetworkSpeedKeys.UploadMbps),
        };

        var proxmox = proxmoxNodes.GetSnapshot().Values.Select(host => new
        {
            hostId = host.HostId,
            displayName = host.DisplayName,
            nodes = host.Items.Select(n => new
            {
                node = n.Node,
                cpuPercent = Get(ProxmoxNodeKeys.CpuUsedPercent(host.HostId, n.Node)),
                memoryPercent = Get(ProxmoxNodeKeys.MemoryUsedPercent(host.HostId, n.Node)),
            }),
        });

        var pbs = pbsDatastores.GetSnapshot().Values.Select(host => new
        {
            hostId = host.HostId,
            displayName = host.DisplayName,
            datastores = host.Items.Select(d => new
            {
                store = d.Store,
                usedPercent = Get(PbsStatKeys.UsedPercent(host.HostId, d.Store)),
            }),
        });

        return Results.Ok(new { system, network, proxmox, pbs });
    }
}
