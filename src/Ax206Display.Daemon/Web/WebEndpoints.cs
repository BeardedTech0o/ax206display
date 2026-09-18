using Ax206Display.Config.Models;
using Ax206Display.Config.Services;
using Ax206Display.Rendering.Compositing;
using Ax206Display.Rendering.Playback;
using Ax206Display.Rendering.Widgets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Ax206Display.Daemon.Web;

/// <summary>
/// The web designer's API: list devices, render an on-demand PNG preview of
/// each one's current layout (reusing the same FrameCompositor/widgets the
/// live display loop uses - see DeviceDisplayLoop.RunAsync - so what you see
/// in the browser matches what's actually on the panel), and config
/// export/import. Read-only widget editing (add/move/resize/delete) is a
/// follow-up milestone; for now layouts are still edited by hand in
/// config.json, same as documented in docs/linux-daemon.md.
/// </summary>
public static partial class WebEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/devices", GetDevicesAsync);
        app.MapGet("/api/devices/{deviceId}/preview.png", GetPreviewAsync);
        app.MapGet("/api/config/export", ExportConfigAsync);
        app.MapPost("/api/config/import", ImportConfigAsync);
    }

    private static async Task<IResult> GetDevicesAsync(ConfigService configService, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        var devices = config.Devices.Select(d => new DeviceSummary(d.Id, d.Name, d.ScreenWidth, d.ScreenHeight, d.Brightness, d.Widgets.Count));
        return Results.Ok(devices);
    }

    private static async Task<IResult> GetPreviewAsync(
        string deviceId, ConfigService configService, IRenderDataProvider dataProvider, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        var device = config.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null)
        {
            return Results.NotFound();
        }

        var logger = loggerFactory.CreateLogger("Ax206Display.Daemon.Web.Preview");
        var placements = BuildPreviewPlacements(device.Id, device.Widgets, logger);
        using var backgroundImage = LoadBackgroundImage(device.BackgroundImagePath, logger);

        var compositor = new FrameCompositor(device.ScreenWidth, device.ScreenHeight);
        var context = new WidgetRenderContext { Now = DateTimeOffset.Now, Data = dataProvider.GetSnapshot() };

        using var frame = compositor.ComposeFrame(placements, context, backgroundImage);
        using var image = SKImage.FromBitmap(frame);
        using var pngData = image.Encode(SKEncodedImageFormat.Png, quality: 100);

        // Never cache a live preview - the whole point is that it reflects
        // the current sensor readings/clock, not a snapshot from whenever
        // the browser first requested it.
        return Results.Bytes(pngData.ToArray(), "image/png");
    }

    private static async Task<IResult> ExportConfigAsync(ConfigService configService, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        var tempPath = Path.Combine(Path.GetTempPath(), $"ax206display-export-{Guid.NewGuid():N}.zip");
        try
        {
            await ConfigPackageService.ExportAsync(config, tempPath, cancellationToken);
            var bytes = await File.ReadAllBytesAsync(tempPath, cancellationToken);
            return Results.File(bytes, "application/zip", "ax206display-config.zip");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static async Task<IResult> ImportConfigAsync(HttpRequest request, ConfigService configService, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest("Expected a multipart/form-data upload with a 'file' field.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest("No file uploaded.");
        }

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"ax206display-import-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var stream = File.Create(tempZipPath))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            var backgroundsDirectory = Path.Combine(
                Path.GetDirectoryName(LinuxPaths.GetSecretStorePath()) ?? "/var/lib/ax206display",
                "imported-backgrounds");

            var imported = await ConfigPackageService.ImportAsync(tempZipPath, backgroundsDirectory, cancellationToken);
            var existing = await configService.LoadAsync(cancellationToken);
            var merged = ConfigPackageService.MergeImported(existing, imported);
            await configService.SaveAsync(merged, cancellationToken);

            return Results.Ok(new
            {
                deviceCount = imported.Devices.Count,
                integrationCount = imported.Integrations.Count,
                note = "Integration passwords/API tokens aren't part of a config package - re-enter them if you imported any integrations.",
            });
        }
        catch (InvalidDataException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        finally
        {
            File.Delete(tempZipPath);
        }
    }

    /// <summary>
    /// Same shape as DisplayManagerHostedService.BuildPlacements (skip a
    /// widget that fails to build rather than fail the whole preview/frame)
    /// - kept as its own small copy rather than shared, since the two call
    /// sites have nothing else in common (one drives a live loop with its
    /// own logger source, this one serves a single on-demand HTTP request).
    /// </summary>
    private static List<WidgetPlacement> BuildPreviewPlacements(string deviceId, IEnumerable<WidgetConfig> widgets, ILogger logger)
    {
        var placements = new List<WidgetPlacement>();
        foreach (var widget in widgets)
        {
            try
            {
                placements.Add(new WidgetPlacement(WidgetFactory.Create(widget), widget.X, widget.Y, widget.ZOrder));
            }
            catch (Exception ex)
            {
                LogWidgetSkipped(logger, ex, widget.Id, deviceId);
            }
        }

        return placements;
    }

    private static SKBitmap? LoadBackgroundImage(string? path, ILogger logger)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            var bitmap = SKBitmap.Decode(path);
            if (bitmap is null)
            {
                LogBackgroundImageDecodeFailed(logger, path);
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            LogBackgroundImageLoadFailed(logger, ex, path);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping widget {WidgetId} for {DeviceId} in preview - failed to build it from saved config.")]
    private static partial void LogWidgetSkipped(ILogger logger, Exception exception, string widgetId, string deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not decode background image '{Path}' for preview.")]
    private static partial void LogBackgroundImageDecodeFailed(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load background image '{Path}' for preview.")]
    private static partial void LogBackgroundImageLoadFailed(ILogger logger, Exception exception, string path);

    private sealed record DeviceSummary(string Id, string Name, int ScreenWidth, int ScreenHeight, int Brightness, int WidgetCount);
}
