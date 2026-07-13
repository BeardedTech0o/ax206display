using Ax206Display.Rendering.Widgets;
using SkiaSharp;

namespace Ax206Display.Tests.Rendering;

public class SystemStatWidgetTests
{
    [Fact]
    public void Render_WithValuePresent_PaintsText()
    {
        var widget = new SystemStatWidget("stat", 160, 60, dataKey: "system.cpu.load", label: "CPU", unit: "%");
        Assert.True(RenderAndCheckForText(widget, new Dictionary<string, object> { ["system.cpu.load"] = 42.5 }));
    }

    [Fact]
    public void Render_WithMissingValue_PaintsPlaceholderInsteadOfThrowing()
    {
        var widget = new SystemStatWidget("stat", 160, 60, dataKey: "system.cpu.load", label: "CPU", unit: "%");
        Assert.True(RenderAndCheckForText(widget, new Dictionary<string, object>()));
    }

    [Fact]
    public void Render_WithNonNumericValue_PaintsPlaceholderInsteadOfThrowing()
    {
        var widget = new SystemStatWidget("stat", 160, 60, dataKey: "system.cpu.load");
        Assert.True(RenderAndCheckForText(widget, new Dictionary<string, object> { ["system.cpu.load"] = "not a number" }));
    }

    [Fact]
    public void Render_AcceptsOtherNumericTypes()
    {
        var widget = new SystemStatWidget("stat", 160, 60, dataKey: "k");
        Assert.True(RenderAndCheckForText(widget, new Dictionary<string, object> { ["k"] = 42 }));
        Assert.True(RenderAndCheckForText(widget, new Dictionary<string, object> { ["k"] = 42.5f }));
        Assert.True(RenderAndCheckForText(widget, new Dictionary<string, object> { ["k"] = 42L }));
    }

    private static bool RenderAndCheckForText(SystemStatWidget widget, Dictionary<string, object> data)
    {
        using var bitmap = new SKBitmap(widget.Width, widget.Height, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        var context = new WidgetRenderContext
        {
            Now = new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero),
            Data = data,
        };

        widget.Render(canvas, context);
        canvas.Flush();

        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.Black)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
