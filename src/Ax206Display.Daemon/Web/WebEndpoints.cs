using System.Text.Json.Nodes;
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
/// in the browser matches what's actually on the panel), edit a device's
/// widgets/background/brightness, and config export/import. A write here
/// only ever updates config.json through ConfigService - it never talks to
/// the live DeviceDisplayLoop directly, because DisplayManagerHostedService
/// already polls config.json every few seconds and hot-reloads changes onto
/// the physical panel (WatchConfigForChangesAsync); reusing that instead of
/// a second push path keeps there being exactly one way layout changes reach
/// a display, matching how hand-editing config.json always worked.
/// </summary>
public static partial class WebEndpoints
{
    private const string UploadedBackgroundsDirectoryName = "uploaded-backgrounds";

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/devices", GetDevicesAsync);
        app.MapGet("/api/devices/{deviceId}", GetDeviceAsync);
        app.MapGet("/api/devices/{deviceId}/preview.png", GetPreviewAsync);
        app.MapPut("/api/devices/{deviceId}/widgets", PutWidgetsAsync);
        app.MapPut("/api/devices/{deviceId}/brightness", PutBrightnessAsync);
        app.MapGet("/api/devices/{deviceId}/background", GetBackgroundAsync);
        app.MapPost("/api/devices/{deviceId}/background", PostBackgroundAsync);
        app.MapDelete("/api/devices/{deviceId}/background", DeleteBackgroundAsync);
        app.MapGet("/api/config/export", ExportConfigAsync);
        app.MapPost("/api/config/import", ImportConfigAsync);
    }

    private static async Task<IResult> GetDevicesAsync(ConfigService configService, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        var devices = config.Devices.Select(d => new DeviceSummary(d.Id, d.Name, d.ScreenWidth, d.ScreenHeight, d.Brightness, d.Widgets.Count));
        return Results.Ok(devices);
    }

    private static async Task<IResult> GetDeviceAsync(string deviceId, ConfigService configService, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        var device = config.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new DeviceDetail(
            device.Id,
            device.Name,
            device.ScreenWidth,
            device.ScreenHeight,
            device.Brightness,
            !string.IsNullOrEmpty(device.BackgroundImagePath),
            device.Widgets.Select(WidgetDto.FromConfig).ToList()));
    }

    private static async Task<IResult> PutWidgetsAsync(
        string deviceId, List<WidgetDto> widgets, ConfigService configService, CancellationToken cancellationToken)
    {
        foreach (var widget in widgets)
        {
            if (string.IsNullOrWhiteSpace(widget.Id) || string.IsNullOrWhiteSpace(widget.Type))
            {
                return Results.BadRequest("Every widget needs an id and a type.");
            }

            if (widget.Width <= 0 || widget.Height <= 0)
            {
                return Results.BadRequest($"Widget '{widget.Id}' must have a positive width and height.");
            }

            if ((widget.Type is "stat" or "gauge") && string.IsNullOrWhiteSpace(widget.Settings?["dataKey"]?.GetValue<string>()))
            {
                return Results.BadRequest($"Widget '{widget.Id}' ({widget.Type}) needs a dataKey setting.");
            }
        }

        return await UpdateDeviceAsync(deviceId, configService, device => device with { Widgets = widgets.Select(w => w.ToConfig()).ToList() }, cancellationToken);
    }

    private static async Task<IResult> PutBrightnessAsync(
        string deviceId, BrightnessDto body, ConfigService configService, CancellationToken cancellationToken)
    {
        var brightness = Math.Clamp(body.Brightness, 0, 7);
        return await UpdateDeviceAsync(deviceId, configService, device => device with { Brightness = brightness }, cancellationToken);
    }

    private static async Task<IResult> GetBackgroundAsync(string deviceId, ConfigService configService, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        var device = config.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null || string.IsNullOrEmpty(device.BackgroundImagePath) || !File.Exists(device.BackgroundImagePath))
        {
            return Results.NotFound();
        }

        var contentType = Path.GetExtension(device.BackgroundImagePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/png",
        };

        return Results.File(await File.ReadAllBytesAsync(device.BackgroundImagePath, cancellationToken), contentType);
    }

    private static async Task<IResult> PostBackgroundAsync(string deviceId, HttpRequest request, ConfigService configService, CancellationToken cancellationToken)
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

        // One fixed slot per device, same reasoning as
        // ConfigPackageService.ImportAsync's background handling: a name
        // that can't accumulate across repeated uploads.
        var deviceDirectory = Path.Combine(
            Path.GetDirectoryName(LinuxPaths.GetSecretStorePath()) ?? "/var/lib/ax206display",
            UploadedBackgroundsDirectoryName,
            ConfigPackageService.SanitizePathComponent(deviceId));
        Directory.CreateDirectory(deviceDirectory);

        var extension = ConfigPackageService.SanitizePathComponent(Path.GetExtension(file.FileName));
        var destinationPath = Path.Combine(deviceDirectory, "background" + extension);

        await using (var stream = File.Create(destinationPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        return await UpdateDeviceAsync(deviceId, configService, device => device with { BackgroundImagePath = destinationPath }, cancellationToken);
    }

    private static async Task<IResult> DeleteBackgroundAsync(string deviceId, ConfigService configService, CancellationToken cancellationToken)
    {
        return await UpdateDeviceAsync(deviceId, configService, device => device with { BackgroundImagePath = null }, cancellationToken);
    }

    private static async Task<IResult> UpdateDeviceAsync(
        string deviceId, ConfigService configService, Func<DeviceProfileConfig, DeviceProfileConfig> mutate, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        var index = config.Devices.FindIndex(d => d.Id == deviceId);
        if (index < 0)
        {
            return Results.NotFound();
        }

        var updatedDevices = new List<DeviceProfileConfig>(config.Devices) { [index] = mutate(config.Devices[index]) };
        await configService.SaveAsync(config with { Devices = updatedDevices }, cancellationToken);
        return Results.Ok();
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

    private sealed record DeviceDetail(string Id, string Name, int ScreenWidth, int ScreenHeight, int Brightness, bool HasBackground, List<WidgetDto> Widgets);

    private sealed record BrightnessDto(int Brightness);

    /// <summary>
    /// Wire shape for a widget over HTTP - a plain DTO rather than exposing
    /// WidgetConfig/WidgetDesignItem directly, so the API's shape doesn't
    /// silently change if either of those internal types ever does.
    /// </summary>
    private sealed record WidgetDto(string Id, string Type, int X, int Y, int Width, int Height, int ZOrder, JsonObject? Settings)
    {
        public static WidgetDto FromConfig(WidgetConfig config) =>
            new(config.Id, config.Type, config.X, config.Y, config.Width, config.Height, config.ZOrder, (JsonObject)config.Settings.DeepClone());

        public WidgetConfig ToConfig() => new()
        {
            Id = Id,
            Type = Type,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            ZOrder = ZOrder,
            Settings = Settings is null ? [] : (JsonObject)Settings.DeepClone(),
        };
    }
}
