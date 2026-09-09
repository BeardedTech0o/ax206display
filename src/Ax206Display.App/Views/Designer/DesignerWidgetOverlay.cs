using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ax206Display.Rendering.Widgets;

namespace Ax206Display.App.Views.Designer;

/// <summary>
/// Everything <see cref="DesignerWidgetOverlay"/> needs from the owning
/// window: selection changes, group-drag lifecycle, and snap-target lookup.
/// Bundled into one object instead of half a dozen constructor parameters.
/// </summary>
internal sealed class DesignerOverlayCallbacks
{
    /// <summary>Replace the whole selection with just this item (plain click on an unselected item).</summary>
    public required Action<WidgetDesignItem> OnSelect { get; init; }

    /// <summary>Add/remove this item from the current selection (Ctrl/Shift+click).</summary>
    public required Action<WidgetDesignItem> OnToggleSelect { get; init; }

    /// <summary>A body drag is starting - capture the current position of every selected item.</summary>
    public required Action OnDragStart { get; init; }

    /// <summary>The anchor item moved by (dx, dy) from its drag-start position (already snap-adjusted) - apply the same delta to every selected item.</summary>
    public required Action<int, int> OnDragMove { get; init; }

    public required Action OnDragEnd { get; init; }

    public required Action OnChanged { get; init; }

    /// <summary>The other widgets' boxes to snap against - queried at drag time so it's always current.</summary>
    public required Func<IReadOnlyList<SnapBox>> GetSnapTargets { get; init; }

    /// <summary>Draws (or, with two nulls, clears) the vertical/horizontal alignment guide lines.</summary>
    public required Action<int?, int?> ShowSnapGuides { get; init; }
}

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
    private readonly DesignerOverlayCallbacks _callbacks;

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

    public DesignerWidgetOverlay(
        WidgetDesignItem item,
        Canvas rootCanvas,
        int canvasWidth,
        int canvasHeight,
        DesignerOverlayCallbacks callbacks)
    {
        _item = item;
        _rootCanvas = rootCanvas;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        _callbacks = callbacks;

        var accentBrush = (Brush)Application.Current.FindResource("AccentBrush");
        var handleBorderBrush = (Brush)Application.Current.FindResource("SurfaceBrush");

        _hitBorder = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(0),
        };
        _hitBorder.MouseLeftButtonDown += OnBodyMouseDown;
        _hitBorder.MouseMove += OnBodyMouseMove;
        _hitBorder.MouseLeftButtonUp += OnBodyMouseUp;
        Children.Add(_hitBorder);

        AddHandle("nw", HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(-HandleSize / 2, -HandleSize / 2, 0, 0), Cursors.SizeNWSE, accentBrush, handleBorderBrush);
        AddHandle("ne", HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -HandleSize / 2, -HandleSize / 2, 0), Cursors.SizeNESW, accentBrush, handleBorderBrush);
        AddHandle("sw", HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(-HandleSize / 2, 0, 0, -HandleSize / 2), Cursors.SizeNESW, accentBrush, handleBorderBrush);
        AddHandle("se", HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -HandleSize / 2, -HandleSize / 2), Cursors.SizeNWSE, accentBrush, handleBorderBrush);

        SyncPosition();
        SetSelected(false, showHandles: false);
    }

    public void SyncPosition()
    {
        Canvas.SetLeft(this, _item.X);
        Canvas.SetTop(this, _item.Y);
        Width = _item.Width;
        Height = _item.Height;
        Panel.SetZIndex(this, _item.ZOrder);
    }

    /// <param name="showHandles">Resize handles only make sense for a single selected widget - a multi-selection shows the selection outline only.</param>
    public void SetSelected(bool selected, bool showHandles)
    {
        IsSelected = selected;
        _hitBorder.BorderThickness = new Thickness(selected ? 2 : 0);
        foreach (var handle in _handles)
        {
            handle.Visibility = selected && showHandles ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void AddHandle(string name, HorizontalAlignment horizontal, VerticalAlignment vertical, Thickness margin, Cursor cursor, Brush fill, Brush borderBrush)
    {
        var handle = new Border
        {
            Width = HandleSize,
            Height = HandleSize,
            Background = fill,
            BorderBrush = borderBrush,
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
        var modifierHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (modifierHeld)
        {
            _callbacks.OnToggleSelect(_item);
            if (!IsSelected)
            {
                // The click just deselected this widget - nothing to drag.
                e.Handled = true;
                return;
            }
        }
        else if (!IsSelected)
        {
            // Clicking an item outside the current selection replaces it.
            // Clicking one that's already part of a multi-selection keeps
            // the whole group selected so the drag below moves it together.
            _callbacks.OnSelect(_item);
        }

        _callbacks.OnDragStart();
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
        var snapped = DesignerSnapEngine.SnapMove(proposed, _callbacks.GetSnapTargets(), _canvasWidth, _canvasHeight);
        _callbacks.ShowSnapGuides(snapped.VerticalGuide, snapped.HorizontalGuide);

        _callbacks.OnDragMove(snapped.X - _dragOriginX, snapped.Y - _dragOriginY);
    }

    private void OnBodyMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingBody = false;
        _hitBorder.ReleaseMouseCapture();
        _callbacks.ShowSnapGuides(null, null);
        _callbacks.OnDragEnd();
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
        _callbacks.OnChanged();
    }

    private void OnHandleMouseUp(object sender, MouseButtonEventArgs e)
    {
        _activeHandle = null;
        ((Border)sender).ReleaseMouseCapture();
    }
}
