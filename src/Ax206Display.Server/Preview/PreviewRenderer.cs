using System.Collections.Concurrent;
using Ax206Display.Config.Models;
using Ax206Display.Rendering.Compositing;
using Ax206Display.Rendering.Playback;
using Ax206Display.Rendering.Widgets;
using SkiaSharp;

namespace Ax206Display.Server.Preview;

/// <summary>
/// Renders a layout to PNG with the exact compositor the display loop uses
/// (including its RGB565 frame, so colors band the way they will on the
/// panel), fed with the same live data. Lets the web editor show unsaved
/// edits, and works with no display plugged in at all.
/// </summary>
public sealed class PreviewRenderer
{
    private readonly IRenderDataProvider _dataProvider;
    private readonly ConcurrentDictionary<string, CachedBackground> _backgrounds = new();

    public PreviewRenderer(IRenderDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public byte[] RenderPng(int width, int height, IEnumerable<WidgetConfig> widgets, string? backgroundImagePath)
    {
        var placements = new List<WidgetPlacement>();
        foreach (var widget in widgets)
        {
            try
            {
                placements.Add(new WidgetPlacement(WidgetFactory.Create(widget), widget.X, widget.Y, widget.ZOrder));
            }
            catch (Exception)
            {
                // Same policy as the display loop: one bad widget is left
                // out rather than blanking the whole preview.
            }
        }

        var context = new WidgetRenderContext { Now = DateTimeOffset.Now, Data = _dataProvider.GetSnapshot() };
        var compositor = new FrameCompositor(width, height);

        using var frame = compositor.ComposeFrame(placements, context, GetBackground(backgroundImagePath));
        using var image = SKImage.FromBitmap(frame);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    /// <summary>
    /// Decoded backgrounds are cached per path and re-read when the file's
    /// write time changes, so the preview's refresh timer isn't decoding the
    /// same image every couple of seconds.
    /// </summary>
    private SKBitmap? GetBackground(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        var lastWrite = File.GetLastWriteTimeUtc(path);
        if (_backgrounds.TryGetValue(path, out var cached) && cached.LastWriteUtc == lastWrite)
        {
            return cached.Bitmap;
        }

        var bitmap = SKBitmap.Decode(path);
        _backgrounds[path] = new CachedBackground(bitmap, lastWrite);

        // The replaced bitmap isn't disposed: a concurrent render may still
        // be drawing it. It's unreferenced now and the GC reclaims it.
        return bitmap;
    }

    private sealed record CachedBackground(SKBitmap? Bitmap, DateTime LastWriteUtc);
}
