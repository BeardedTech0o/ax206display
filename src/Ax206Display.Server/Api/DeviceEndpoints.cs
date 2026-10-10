using System.Security.Cryptography;
using System.Text;
using Ax206Display.Config.Models;
using Ax206Display.Config.Services;
using Ax206Display.Engine.Composition;
using Ax206Display.Engine.Services;
using Ax206Display.Rendering.Widgets;
using Ax206Display.Server.Preview;
using SkiaSharp;

namespace Ax206Display.Server.Api;

public sealed record DeviceDto(
    string Id,
    string Name,
    int ScreenWidth,
    int ScreenHeight,
    int Brightness,
    int TargetFps,
    bool HasBackgroundImage,
    bool Connected,
    IReadOnlyList<WidgetConfig> Widgets);

public sealed record DeviceUpdateRequest(string? Name, int Brightness, int TargetFps, List<WidgetConfig>? Widgets);

public sealed record PreviewRequest(List<WidgetConfig>? Widgets);

/// <summary>
/// Layout management for each display. Edits go to config.json; the display
/// manager's config watcher picks them up within a few seconds, same as an
/// edit from the Windows Widget Designer.
/// </summary>
public static class DeviceEndpoints
{
    public const int MinFps = 1;
    public const int MaxFps = 30;
    public const int MaxBackgroundUploadBytes = 20 * 1024 * 1024;
    private const int MaxImageSide = 16384;
    private const long MaxImagePixels = 64L * 1024 * 1024;
    private const int MaxWidgets = 200;
    private const int MaxWidgetDimension = 4096;

    public static void MapDeviceEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/devices", ListDevicesAsync);
        api.MapPost("/devices/refresh", RefreshAsync);
        api.MapPut("/devices/{id}", UpdateDeviceAsync);
        api.MapDelete("/devices/{id}", ForgetDeviceAsync);
        api.MapPost("/devices/{id}/preview", PreviewAsync);
        api.MapPut("/devices/{id}/background", UploadBackgroundAsync);
        api.MapDelete("/devices/{id}/background", RemoveBackgroundAsync);
    }

    private static async Task<IResult> ListDevicesAsync(ConfigService configService, DisplayManagerHostedService displayManager, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        var connected = displayManager.ConnectedDeviceIds.ToHashSet(StringComparer.Ordinal);
        return Results.Ok(config.Devices.Select(d => ToDto(d, connected.Contains(d.Id))));
    }

    private static async Task<IResult> RefreshAsync(DisplayManagerHostedService displayManager, CancellationToken cancellationToken)
    {
        var added = await displayManager.RefreshDevicesAsync(cancellationToken);
        return Results.Ok(new { newDevices = added });
    }

    private static async Task<IResult> UpdateDeviceAsync(string id, DeviceUpdateRequest request, ConfigService configService, DisplayManagerHostedService displayManager, CancellationToken cancellationToken)
    {
        var validationError = ValidateWidgets(request.Widgets);
        if (validationError is not null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        DeviceProfileConfig? updatedProfile = null;
        await configService.UpdateAsync(
            config =>
            {
                var existing = config.Devices.FirstOrDefault(d => d.Id == id);
                if (existing is null)
                {
                    return config;
                }

                updatedProfile = existing with
                {
                    Name = string.IsNullOrWhiteSpace(request.Name) ? existing.Name : request.Name.Trim(),
                    Brightness = Math.Clamp(request.Brightness, 0, 7),
                    TargetFps = Math.Clamp(request.TargetFps, MinFps, MaxFps),
                    Widgets = request.Widgets ?? existing.Widgets,
                };
                return config with { Devices = config.Devices.Select(d => d.Id == id ? updatedProfile : d).ToList() };
            },
            cancellationToken);

        return updatedProfile is null
            ? Results.NotFound()
            : Results.Ok(ToDto(updatedProfile, displayManager.ConnectedDeviceIds.Contains(id)));
    }

    /// <summary>
    /// Only offered for a display that isn't connected: removing a live
    /// display's profile would leave its loop running with a layout nobody
    /// can edit, then re-provision it as brand new on the next restart.
    /// </summary>
    private static async Task<IResult> ForgetDeviceAsync(string id, ConfigService configService, DisplayManagerHostedService displayManager, Ax206DisplayPaths paths, CancellationToken cancellationToken)
    {
        if (displayManager.ConnectedDeviceIds.Contains(id))
        {
            return Results.Conflict(new { error = "Unplug the display before forgetting it." });
        }

        DeviceProfileConfig? removed = null;
        await configService.UpdateAsync(
            config =>
            {
                removed = config.Devices.FirstOrDefault(d => d.Id == id);
                return config with { Devices = config.Devices.Where(d => d.Id != id).ToList() };
            },
            cancellationToken);

        if (removed is null)
        {
            return Results.NotFound();
        }

        DeleteIfManaged(paths, removed.BackgroundImagePath);
        return Results.NoContent();
    }

    private static async Task<IResult> PreviewAsync(string id, PreviewRequest request, ConfigService configService, PreviewRenderer renderer, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        var profile = config.Devices.FirstOrDefault(d => d.Id == id);
        if (profile is null)
        {
            return Results.NotFound();
        }

        var validationError = ValidateWidgets(request.Widgets);
        if (validationError is not null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        var png = renderer.RenderPng(profile.ScreenWidth, profile.ScreenHeight, request.Widgets ?? profile.Widgets, profile.BackgroundImagePath);
        return Results.File(png, "image/png");
    }

    /// <summary>
    /// Takes the raw image as the request body (no multipart form). The image
    /// is decoded, scaled to the panel's resolution and re-encoded as PNG
    /// before it's stored, so only a known-good image of a sensible size ever
    /// lands on disk, and the display loop never decodes a 12-megapixel
    /// phone photo on a Pi.
    /// </summary>
    private static async Task<IResult> UploadBackgroundAsync(string id, HttpRequest httpRequest, ConfigService configService, Ax206DisplayPaths paths, CancellationToken cancellationToken)
    {
        var config = await configService.LoadAsync(cancellationToken);
        var profile = config.Devices.FirstOrDefault(d => d.Id == id);
        if (profile is null)
        {
            return Results.NotFound();
        }

        if (httpRequest.ContentLength is > MaxBackgroundUploadBytes)
        {
            return Results.BadRequest(new { error = "Image is too large (20 MB max)." });
        }

        using var buffer = new MemoryStream();
        await httpRequest.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length is 0 or > MaxBackgroundUploadBytes)
        {
            return Results.BadRequest(new { error = "Image is empty or too large (20 MB max)." });
        }

        // Read just the header first. A few KB of PNG can declare tens of
        // thousands of pixels per side, and decoding that would exhaust a
        // Pi's memory long before the 20 MB byte cap mattered.
        using (var data = SKData.CreateCopy(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)))
        using (var codec = SKCodec.Create(data))
        {
            if (codec is null)
            {
                return Results.BadRequest(new { error = "That file isn't an image this server can read (try PNG or JPEG)." });
            }

            if (!IsAcceptableImageSize(codec.Info.Width, codec.Info.Height))
            {
                return Results.BadRequest(new { error = "That image has too many pixels. Use a smaller one (under 64 megapixels)." });
            }
        }

        buffer.Position = 0;
        using var decoded = SKBitmap.Decode(buffer);
        if (decoded is null)
        {
            return Results.BadRequest(new { error = "That file isn't an image this server can read (try PNG or JPEG)." });
        }

        using var scaled = decoded.Resize(new SKImageInfo(profile.ScreenWidth, profile.ScreenHeight), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("Could not scale the uploaded image.");
        using var image = SKImage.FromBitmap(scaled);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        // A new file name per upload: the display manager only re-decodes a
        // background when the configured path changes, so overwriting the
        // same file in place would leave the old image on the panel.
        var path = NewBackgroundPath(paths, id);
        Directory.CreateDirectory(paths.BackgroundImagesDirectory);
        var tempPath = path + ".tmp";
        await using (var file = File.Create(tempPath))
        {
            encoded.SaveTo(file);
        }

        File.Move(tempPath, path, overwrite: true);

        string? previousPath = null;
        await configService.UpdateAsync(
            c =>
            {
                previousPath = c.Devices.FirstOrDefault(d => d.Id == id)?.BackgroundImagePath;
                return WithBackground(c, id, path);
            },
            cancellationToken);

        DeleteIfManaged(paths, previousPath);
        return Results.NoContent();
    }

    /// <summary>
    /// Whether a declared image size is small enough to decode safely. Generous
    /// enough for a 48-megapixel phone photo; far below a decompression bomb.
    /// </summary>
    public static bool IsAcceptableImageSize(int width, int height) =>
        width is > 0 and <= MaxImageSide
        && height is > 0 and <= MaxImageSide
        && (long)width * height <= MaxImagePixels;

    private static async Task<IResult> RemoveBackgroundAsync(string id, ConfigService configService, Ax206DisplayPaths paths, CancellationToken cancellationToken)
    {
        string? previousPath = null;
        await configService.UpdateAsync(
            c =>
            {
                previousPath = c.Devices.FirstOrDefault(d => d.Id == id)?.BackgroundImagePath;
                return WithBackground(c, id, null);
            },
            cancellationToken);

        DeleteIfManaged(paths, previousPath);
        return Results.NoContent();
    }

    private static AppConfig WithBackground(AppConfig config, string id, string? path) =>
        config with { Devices = config.Devices.Select(d => d.Id == id ? d with { BackgroundImagePath = path } : d).ToList() };

    /// <summary>Device IDs are USB serials and may hold anything; the file name is a hash of one, never the raw string.</summary>
    private static string NewBackgroundPath(Ax206DisplayPaths paths, string deviceId)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId)))[..24].ToLowerInvariant();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(paths.BackgroundImagesDirectory, $"{name}-{suffix}.png");
    }

    /// <summary>
    /// Deletes a background only if this server stored it. A config carried
    /// over from Windows can point anywhere on disk, and that file belongs to
    /// the user.
    /// </summary>
    private static void DeleteIfManaged(Ax206DisplayPaths paths, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var managedRoot = Path.GetFullPath(paths.BackgroundImagesDirectory) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(path).StartsWith(managedRoot, StringComparison.Ordinal))
        {
            TryDelete(path);
        }
    }

    private static string? ValidateWidgets(List<WidgetConfig>? widgets)
    {
        if (widgets is null)
        {
            return null;
        }

        if (widgets.Count > MaxWidgets)
        {
            return $"A layout can hold at most {MaxWidgets} widgets.";
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var widget in widgets)
        {
            if (string.IsNullOrWhiteSpace(widget.Id) || !ids.Add(widget.Id))
            {
                return "Every widget needs a unique id.";
            }

            if (widget.Width is < 1 or > MaxWidgetDimension || widget.Height is < 1 or > MaxWidgetDimension)
            {
                return $"Widget '{widget.Id}' has an invalid size.";
            }

            try
            {
                WidgetFactory.Create(widget);
            }
            catch (Exception ex)
            {
                return $"Widget '{widget.Id}': {ex.Message}";
            }
        }

        return null;
    }

    private static DeviceDto ToDto(DeviceProfileConfig profile, bool connected) =>
        new(
            profile.Id,
            profile.Name,
            profile.ScreenWidth,
            profile.ScreenHeight,
            profile.Brightness,
            profile.TargetFps,
            !string.IsNullOrEmpty(profile.BackgroundImagePath) && File.Exists(profile.BackgroundImagePath),
            connected,
            profile.Widgets);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a leftover image is harmless.
        }
    }
}
