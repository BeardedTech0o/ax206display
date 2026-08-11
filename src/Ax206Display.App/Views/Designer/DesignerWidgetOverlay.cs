using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ax206Display.Rendering.Widgets;

namespace Ax206Display.App.Views.Designer;

/// <summary>
/// A transparent, click-to-select / drag-to-move / corner-handle-to-resize
/// hit region for one widget, laid over the real rendered preview. Purely
/// interaction chrome - the actual pixels come from the SkiaSharp compositor
/// underneath; this never draws widget content itself.
/// </summary>
internal sealed class DesignerWidgetOverlay : Grid
{
    private const double HandleSize = 8;
    private const int MinWidgetSize = 10;

    private readonly WidgetDesignItem _item;
    private readonly Canvas _rootCanvas;
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;
    private readonly Action<WidgetDesignItem> _onSelect;
    private readonly Action _onChanged;
    private readonly Func<IReadOnlyList<SnapBox>> _getSnapTargets;
    private readonly Action<int?, int?> _showSnapGuides;

    private readonly Border _hitBorder;
    private readonly List<Border> _handles = [];

    private bool _isDraggingBody;
    private string? _activeHandle;
    private Point _dragStart;
    private int _dragOriginX;
    private int _dragOriginY;
    private int _dragOriginWidth;
    private int _dragOriginHeight;

    public bool IsSelected { get; private set; }

    /// <param name="getSnapTargets">The other widgets' boxes to snap against - queried at drag time so it's always current.</param>
    /// <param name="showSnapGuides">Draws (or, with two nulls, clears) the vertical/horizontal alignment guide lines.</param>
    public DesignerWidgetOverlay(
        WidgetDesignItem item,
        Canvas rootCanvas,
        int canvasWidth,
        int canvasHeight,
        Action<WidgetDesignItem> onSelect,
        Action onChanged,
        Func<IReadOnlyList<SnapBox>> getSnapTargets,
        Action<int?, int?> showSnapGuides)
    {
        _item = item;
        _rootCanvas = rootCanvas;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        _onSelect = onSelect;
        _onChanged = onChanged;
        _getSnapTargets = getSnapTargets;
        _showSnapGuides = showSnapGuides;

        _hitBorder = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.DeepSkyBlue,
            BorderThickness = new Thickness(0),
        };
        _hitBorder.MouseLeftButtonDown += OnBodyMouseDown;
        _hitBorder.MouseMove += OnBodyMouseMove;
        _hitBorder.MouseLeftButtonUp += OnBodyMouseUp;
        Children.Add(_hitBorder);

        AddHandle("nw", HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(-HandleSize / 2, -HandleSize / 2, 0, 0), Cursors.SizeNWSE);
        AddHandle("ne", HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -HandleSize / 2, -HandleSize / 2, 0), Cursors.SizeNESW);
        AddHandle("sw", HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(-HandleSize / 2, 0, 0, -HandleSize / 2), Cursors.SizeNESW);
        AddHandle("se", HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -HandleSize / 2, -HandleSize / 2), Cursors.SizeNWSE);

        SyncPosition();
        SetSelected(false);
    }

    public void SyncPosition()
    {
        Canvas.SetLeft(this, _item.X);
        Canvas.SetTop(this, _item.Y);
        Width = _item.Width;
        Height = _item.Height;
        Panel.SetZIndex(this, _item.ZOrder);
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        _hitBorder.BorderThickness = new Thickness(selected ? 2 : 0);
        foreach (var handle in _handles)
        {
            handle.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void AddHandle(string name, HorizontalAlignment horizontal, VerticalAlignment vertical, Thickness margin, Cursor cursor)
    {
        var handle = new Border
        {
            Width = HandleSize,
            Height = HandleSize,
            Background = Brushes.DeepSkyBlue,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Margin = margin,
            Cursor = cursor,
            Visibility = Visibility.Collapsed,
            Tag = name,
        };
        handle.MouseLeftButtonDown += OnHandleMouseDown;
        handle.MouseMove += OnHandleMouseMove;
        handle.MouseLeftButtonUp += OnHandleMouseUp;

        _handles.Add(handle);
        Children.Add(handle);
    }

    private void OnBodyMouseDown(object sender, MouseButtonEventArgs e)
    {
        _onSelect(_item);
        _isDraggingBody = true;
        _dragStart = e.GetPosition(_rootCanvas);
        _dragOriginX = _item.X;
        _dragOriginY = _item.Y;
        _hitBorder.CaptureMouse();
        e.Handled = true;
    }

    private void OnBodyMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingBody)
        {
            return;
        }

        var pos = e.GetPosition(_rootCanvas);
        var dx = (int)Math.Round(pos.X - _dragStart.X);
        var dy = (int)Math.Round(pos.Y - _dragStart.Y);

        var proposed = new SnapBox(_dragOriginX + dx, _dragOriginY + dy, _item.Width, _item.Height);
        var snapped = DesignerSnapEngine.SnapMove(proposed, _getSnapTargets(), _canvasWidth, _canvasHeight);
        _showSnapGuides(snapped.VerticalGuide, snapped.HorizontalGuide);

        // Clamp after snapping so a snap can never push the widget off-canvas.
        var newX = Math.Clamp(snapped.X, 0, Math.Max(0, _canvasWidth - _item.Width));
        var newY = Math.Clamp(snapped.Y, 0, Math.Max(0, _canvasHeight - _item.Height));

        _item.X = newX;
        _item.Y = newY;
        Canvas.SetLeft(this, newX);
        Canvas.SetTop(this, newY);
        _onChanged();
    }

    private void OnBodyMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingBody = false;
        _hitBorder.ReleaseMouseCapture();
        _showSnapGuides(null, null);
    }

    private void OnHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        var handle = (Border)sender;
        _activeHandle = (string)handle.Tag;
        _dragStart = e.GetPosition(_rootCanvas);
        _dragOriginX = _item.X;
        _dragOriginY = _item.Y;
        _dragOriginWidth = _item.Width;
        _dragOriginHeight = _item.Height;
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void OnHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (_activeHandle is null)
        {
            return;
        }

        var pos = e.GetPosition(_rootCanvas);
        var dx = (int)Math.Round(pos.X - _dragStart.X);
        var dy = (int)Math.Round(pos.Y - _dragStart.Y);

        var (rawX, rawY, rawWidth, rawHeight) = _activeHandle switch
        {
            "nw" => (_dragOriginX + dx, _dragOriginY + dy, _dragOriginWidth - dx, _dragOriginHeight - dy),
            "ne" => (_dragOriginX, _dragOriginY + dy, _dragOriginWidth + dx, _dragOriginHeight - dy),
            "sw" => (_dragOriginX + dx, _dragOriginY, _dragOriginWidth - dx, _dragOriginHeight + dy),
            "se" => (_dragOriginX, _dragOriginY, _dragOriginWidth + dx, _dragOriginHeight + dy),
            _ => (_item.X, _item.Y, _item.Width, _item.Height),
        };

        var newWidth = Math.Max(MinWidgetSize, rawWidth);
        var newHeight = Math.Max(MinWidgetSize, rawHeight);
        var newX = Math.Clamp(rawX, 0, _canvasWidth - MinWidgetSize);
        var newY = Math.Clamp(rawY, 0, _canvasHeight - MinWidgetSize);
        newWidth = Math.Min(newWidth, _canvasWidth - newX);
        newHeight = Math.Min(newHeight, _canvasHeight - newY);

        _item.X = newX;
        _item.Y = newY;
        _item.Width = newWidth;
        _item.Height = newHeight;

        Canvas.SetLeft(this, newX);
        Canvas.SetTop(this, newY);
        Width = newWidth;
        Height = newHeight;
        _onChanged();
    }

    private void OnHandleMouseUp(object sender, MouseButtonEventArgs e)
    {
        _activeHandle = null;
        ((Border)sender).ReleaseMouseCapture();
    }
}
