using System.Windows;
using System.Windows.Threading;
using Ax206Display.Rendering.Compositing;
using Ax206Display.Rendering.Widgets;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Ax206Display.App.Views;

public partial class WidgetDesignerWindow : Window
{
    private const int PreviewWidth = 480;
    private const int PreviewHeight = 320;

    private readonly FrameCompositor _compositor;
    private readonly ClockWidget _clockWidget;
    private readonly DispatcherTimer _timer;

    public WidgetDesignerWindow()
    {
        InitializeComponent();

        _compositor = new FrameCompositor(PreviewWidth, PreviewHeight);
        _clockWidget = new ClockWidget("preview-clock", PreviewWidth, PreviewHeight);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => PreviewElement.InvalidateVisual();
        _timer.Start();

        Closed += (_, _) => _timer.Stop();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var placements = new[] { new WidgetPlacement(_clockWidget, 0, 0, ZOrder: 0) };
        var context = new WidgetRenderContext { Now = DateTimeOffset.Now, Data = new Dictionary<string, object>() };

        using var frame = _compositor.ComposeFrame(placements, context);

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Black);
        canvas.DrawBitmap(frame, new SKPoint((e.Info.Width - frame.Width) / 2f, (e.Info.Height - frame.Height) / 2f));
    }
}
