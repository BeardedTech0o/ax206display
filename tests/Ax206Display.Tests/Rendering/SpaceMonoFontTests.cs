using Ax206Display.Config.Models;
using Ax206Display.Rendering.Widgets;
using SkiaSharp;

namespace Ax206Display.Tests.Rendering;

/// <summary>
/// "Space Mono" (WidgetCatalog.FontFamilies) isn't a Windows system font, so
/// WidgetTextRenderer bundles it as an embedded resource instead of
/// resolving it by family name like every other listed font - if that
/// embedded .ttf ever failed to load, SkiaSharp would silently fall back to
/// the default typeface and these widgets would render as if no font
/// family had been chosen at all, with no error to notice.
/// </summary>
public class SpaceMonoFontTests
{
    [Fact]
    public void SpaceMonoFontFamily_RendersDifferentlyFromDefault_ConfirmingItActuallyLoaded()
    {
        var withSpaceMono = CountPaintedPixels("Space Mono");
        var withDefault = CountPaintedPixels(fontFamily: null);

        Assert.NotEqual(withDefault, withSpaceMono);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SpaceMonoFontFamily_EveryBoldItalicCombination_RendersWithoutThrowing(bool bold, bool italic)
    {
        var config = MakeConfig("Space Mono", bold, italic);
        var widget = WidgetFactory.Create(config);

        using var bitmap = new SKBitmap(400, 120, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        widget.Render(canvas, new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
            Data = new Dictionary<string, object>(),
        });
    }

    [Fact]
    public void SpaceMonoFontFamily_IsCaseInsensitive()
    {
        var mixedCase = CountPaintedPixels("space mono");
        var canonical = CountPaintedPixels("Space Mono");

        // Same embedded font either way, so the same text at the same fixed
        // size should paint the identical pixel count.
        Assert.Equal(canonical, mixedCase);
    }

    private static int CountPaintedPixels(string? fontFamily)
    {
        var config = MakeConfig(fontFamily, bold: false, italic: false);
        var widget = WidgetFactory.Create(config);

        using var bitmap = new SKBitmap(400, 120, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        widget.Render(canvas, new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
            Data = new Dictionary<string, object>(),
        });
        canvas.Flush();

        var painted = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.Black)
                {
                    painted++;
                }
            }
        }

        return painted;
    }

    private static WidgetConfig MakeConfig(string? fontFamily, bool bold, bool italic)
    {
        var config = new WidgetConfig
        {
            Id = "text-1",
            Type = "text",
            X = 0,
            Y = 0,
            Width = 400,
            Height = 120,
        };
        config.Settings["text"] = "Hello World 123";
        config.Settings["fontSizePx"] = 40;
        config.Settings["bold"] = bold;
        config.Settings["italic"] = italic;
        if (fontFamily is not null)
        {
            config.Settings["fontFamily"] = fontFamily;
        }

        return config;
    }
}
