using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ax206Display.App.Services;
using Ax206Display.App.Views.Designer;
using Ax206Display.Config.Models;
using Ax206Display.Config.Services;
using Ax206Display.DataSources.Proxmox;
using Ax206Display.Rendering.Compositing;
using Ax206Display.Rendering.Playback;
using Ax206Display.Rendering.Widgets;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Ax206Display.App.Views;

/// <summary>
/// A WYSIWYG per-device layout editor: pick a device, add clock/text/stat
/// widgets, drag to move and drag corner handles to resize, edit each
/// widget's settings from dropdowns/text fields (never raw JSON), and save
/// back to config. The preview is composed with the same
/// <see cref="FrameCompositor"/> that drives the real display, so what's
/// shown here is what appears on the panel.
/// </summary>
public partial class WidgetDesignerWindow : Window
{
    private readonly ConfigService _configService;
    private readonly IRenderDataProvider _dataProvider;
    private readonly ProxmoxGuestDirectory _proxmoxGuestDirectory;
    private readonly ProxmoxNodeDirectory _proxmoxNodeDirectory;
    private readonly DisplayManagerHostedService _displayManager;
    private readonly DispatcherTimer _timer;

    private readonly List<WidgetDesignItem> _items = [];
    private readonly Dictionary<WidgetDesignItem, DesignerWidgetOverlay> _overlays = [];

    private AppConfig _config = new();
    private DeviceProfileConfig? _selectedDevice;
    private WidgetDesignItem? _selectedItem;
    private FrameCompositor? _compositor;
    private TextBlock? _positionText;
    private bool _isLoadingDevice;
    private SKBitmap? _backgroundImage;
    private string? _backgroundImagePath;
    private bool _showGrid;
    private System.Windows.Shapes.Line? _verticalGuideLine;
    private System.Windows.Shapes.Line? _horizontalGuideLine;

    public WidgetDesignerWindow(ConfigService configService, IRenderDataProvider dataProvider, ProxmoxGuestDirectory proxmoxGuestDirectory, ProxmoxNodeDirectory proxmoxNodeDirectory, DisplayManagerHostedService displayManager)
    {
        InitializeComponent();
        Theme.DarkTitleBar.Apply(this);
        _configService = configService;
        _dataProvider = dataProvider;
        _proxmoxGuestDirectory = proxmoxGuestDirectory;
        _proxmoxNodeDirectory = proxmoxNodeDirectory;
        _displayManager = displayManager;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => PreviewElement.InvalidateVisual();
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _backgroundImage?.Dispose();
        };

        Loaded += async (_, _) => await LoadConfigAsync();
    }

    private async Task LoadConfigAsync()
    {
        // Preserved across a Refresh so re-scanning for newly plugged-in
        // devices doesn't bounce the user back to the first device in the
        // list if they had a different one selected.
        var previouslySelectedId = _selectedDevice?.Id;

        _config = await _configService.LoadAsync();
        DeviceComboBox.ItemsSource = _config.Devices;

        if (_config.Devices.Count == 0)
        {
            SetStatus("No devices found yet. Connect a display and click Refresh.");
            SetToolbarEnabled(false);
            RenameDeviceButton.IsEnabled = false;
            RemoveDeviceButton.IsEnabled = false;
            return;
        }

        SetToolbarEnabled(true);

        var indexToSelect = previouslySelectedId is null
            ? 0
            : Math.Max(0, _config.Devices.FindIndex(d => d.Id == previouslySelectedId));
        DeviceComboBox.SelectedIndex = indexToSelect;
    }

    private async void OnRefreshDevicesClick(object sender, RoutedEventArgs e)
    {
        RefreshDevicesButton.IsEnabled = false;
        SetStatus("Searching for devices...");
        try
        {
            var newDeviceCount = await _displayManager.RefreshDevicesAsync();
            await LoadConfigAsync();

            SetStatus(newDeviceCount switch
            {
                0 => "No new devices found.",
                1 => "Found 1 new device.",
                _ => $"Found {newDeviceCount} new devices.",
            });
        }
        catch (Exception ex)
        {
            SetStatus("Could not search for devices: " + ex.Message);
        }
        finally
        {
            RefreshDevicesButton.IsEnabled = true;
        }
    }

    private async void OnRenameDeviceClick(object sender, RoutedEventArgs e)
    {
        if (_selectedDevice is null)
        {
            return;
        }

        var newName = PromptForText("Rename Display", "New name:", _selectedDevice.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == _selectedDevice.Name)
        {
            return;
        }

        try
        {
            var freshConfig = await _configService.LoadAsync();
            var index = freshConfig.Devices.FindIndex(d => d.Id == _selectedDevice.Id);
            if (index < 0)
            {
                SetStatus("This device is no longer in the config - nothing was renamed.");
                return;
            }

            var updatedDevices = new List<DeviceProfileConfig>(freshConfig.Devices);
            updatedDevices[index] = updatedDevices[index] with { Name = newName.Trim() };
            await _configService.SaveAsync(freshConfig with { Devices = updatedDevices });

            await LoadConfigAsync();
            SetStatus("Renamed.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not rename: " + ex.Message);
        }
    }

    /// <summary>Minimal modal text-input dialog (WPF has no built-in one). Returns null on cancel.</summary>
    private string? PromptForText(string title, string label, string initialValue)
    {
        var textBox = new TextBox { Text = initialValue, Margin = new Thickness(0, 4, 0, 12), MinWidth = 260 };
        textBox.SelectAll();

        var okButton = new Button { Content = "OK", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", Padding = new Thickness(16, 4, 16, 4), IsCancel = true };

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);

        var rootPanel = new StackPanel { Margin = new Thickness(12) };
        rootPanel.Children.Add(new TextBlock { Text = label });
        rootPanel.Children.Add(textBox);
        rootPanel.Children.Add(buttonRow);

        var dialog = new Window
        {
            Title = title,
            Content = rootPanel,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            // Explicit rather than relying on ForgeTheme.xaml's implicit
            // Window style - a plain "new Window()" is exactly type Window,
            // not a subclass, so implicit TargetType="Window" resolution
            // should apply automatically, but WidgetDesignerWindow/
            // IntegrationsWindow (subclasses of Window) were observed NOT
            // picking up that implicit style, so this is set explicitly
            // here too rather than trusting it.
            Background = Background,
            Foreground = Foreground,
            FontFamily = FontFamily,
        };
        Theme.DarkTitleBar.Apply(dialog);

        okButton.Click += (_, _) => dialog.DialogResult = true;
        textBox.Loaded += (_, _) => textBox.Focus();

        return dialog.ShowDialog() == true ? textBox.Text : null;
    }

    /// <summary>
    /// Deletes the selected device's saved profile (layout, background,
    /// brightness) from config - the only GUI way to clear out a stale entry,
    /// e.g. one left behind by a serial-number collision fix that changed how
    /// an already-known device's ID is computed. Confirmed first since a
    /// saved layout can't be recovered once removed. If the physical display
    /// is still plugged in, it reappears as a "new" device (fresh default
    /// layout) the next time it's discovered - config has no way to tell
    /// "unplugged for good" from "temporarily disconnected".
    /// </summary>
    private async void OnRemoveDeviceClick(object sender, RoutedEventArgs e)
    {
        if (_selectedDevice is null)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            $"Remove '{_selectedDevice.Name}' and its saved layout? This can't be undone.",
            "Remove Device",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            return;
        }

        RemoveDeviceButton.IsEnabled = false;
        try
        {
            var freshConfig = await _configService.LoadAsync();
            var updatedDevices = freshConfig.Devices.Where(d => d.Id != _selectedDevice.Id).ToList();
            await _configService.SaveAsync(freshConfig with { Devices = updatedDevices });

            _selectedDevice = null;
            await LoadConfigAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Could not remove device: " + ex.Message);
            RemoveDeviceButton.IsEnabled = true;
        }
    }

    private void SetToolbarEnabled(bool enabled)
    {
        DeviceComboBox.IsEnabled = enabled;
        AddClockButton.IsEnabled = enabled;
        AddTextButton.IsEnabled = enabled;
        AddStatButton.IsEnabled = enabled;
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceComboBox.SelectedItem is DeviceProfileConfig device)
        {
            LoadDevice(device);
        }
    }

    private void LoadDevice(DeviceProfileConfig device)
    {
        _isLoadingDevice = true;

        _selectedDevice = device;
        _selectedItem = null;
        SaveButton.IsEnabled = false;
        RenameDeviceButton.IsEnabled = true;
        RemoveDeviceButton.IsEnabled = true;

        _items.Clear();
        _items.AddRange(device.Widgets.Select(WidgetDesignItem.FromConfig));

        _compositor = new FrameCompositor(device.ScreenWidth, device.ScreenHeight);
        DesignerRoot.Width = device.ScreenWidth;
        DesignerRoot.Height = device.ScreenHeight;

        LoadBackgroundImage(device.BackgroundImagePath);
        BrightnessSlider.Value = device.Brightness;

        RebuildOverlays();
        ShowEmptyPropertyPanel();
        SetStatus($"{device.ScreenWidth}x{device.ScreenHeight}");
        PreviewElement.InvalidateVisual();

        _isLoadingDevice = false;
    }

    private void RebuildOverlays()
    {
        OverlayCanvas.Children.Clear();
        _overlays.Clear();
        _verticalGuideLine = null;
        _horizontalGuideLine = null;
        OverlayCanvas.Width = DesignerRoot.Width;
        OverlayCanvas.Height = DesignerRoot.Height;

        foreach (var item in _items)
        {
            AddOverlayFor(item);
        }
    }

    private void AddOverlayFor(WidgetDesignItem item)
    {
        var overlay = new DesignerWidgetOverlay(
            item,
            OverlayCanvas,
            (int)DesignerRoot.Width,
            (int)DesignerRoot.Height,
            SelectItem,
            OnItemChanged,
            () => GetSnapTargetsExcept(item),
            ShowSnapGuides);
        _overlays[item] = overlay;
        OverlayCanvas.Children.Add(overlay);
    }

    private List<SnapBox> GetSnapTargetsExcept(WidgetDesignItem draggedItem)
    {
        return _items
            .Where(i => !ReferenceEquals(i, draggedItem))
            .Select(i => new SnapBox(i.X, i.Y, i.Width, i.Height))
            .ToList();
    }

    /// <summary>
    /// Draws Canva-style alignment guides while a widget is being dragged:
    /// a full-height line at the snapped x and/or a full-width line at the
    /// snapped y, cleared (both null) when the drag ends or nothing snaps.
    /// </summary>
    private void ShowSnapGuides(int? verticalX, int? horizontalY)
    {
        if (_verticalGuideLine is not null)
        {
            OverlayCanvas.Children.Remove(_verticalGuideLine);
            _verticalGuideLine = null;
        }

        if (_horizontalGuideLine is not null)
        {
            OverlayCanvas.Children.Remove(_horizontalGuideLine);
            _horizontalGuideLine = null;
        }

        if (verticalX is { } x)
        {
            _verticalGuideLine = MakeGuideLine(x, 0, x, DesignerRoot.Height);
            OverlayCanvas.Children.Add(_verticalGuideLine);
        }

        if (horizontalY is { } y)
        {
            _horizontalGuideLine = MakeGuideLine(0, y, DesignerRoot.Width, y);
            OverlayCanvas.Children.Add(_horizontalGuideLine);
        }
    }

    private static System.Windows.Shapes.Line MakeGuideLine(double x1, double y1, double x2, double y2)
    {
        var line = new System.Windows.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = Brushes.Magenta,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            IsHitTestVisible = false,
        };
        Panel.SetZIndex(line, int.MaxValue);
        return line;
    }

    private void SelectItem(WidgetDesignItem item)
    {
        if (_selectedItem is not null && _overlays.TryGetValue(_selectedItem, out var previousOverlay))
        {
            previousOverlay.SetSelected(false);
        }

        _selectedItem = item;
        _overlays[item].SetSelected(true);
        DeleteButton.IsEnabled = true;
        BuildPropertyPanel(item);
    }

    private void OnItemChanged()
    {
        if (_isLoadingDevice)
        {
            return;
        }

        SaveButton.IsEnabled = true;
        PreviewElement.InvalidateVisual();

        if (_selectedItem is not null)
        {
            RefreshPositionText(_selectedItem);
        }
    }

    /// <summary>
    /// Nudges the selected widget with the arrow keys instead of requiring a
    /// mouse drag for small position tweaks: 1px plain, 5px with Shift, 10px
    /// with Ctrl, 20px with Ctrl+Shift - four fixed step sizes reachable
    /// through the two modifier keys WPF doesn't already claim for something
    /// else in this window. Skipped while focus is on a control that uses
    /// the arrow keys itself (a TextBox's cursor, a ComboBox's selection,
    /// the brightness Slider) so this never fights normal editing.
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_selectedItem is null || !_overlays.TryGetValue(_selectedItem, out var overlay))
        {
            return;
        }

        var (dx, dy) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };

        if (dx == 0 && dy == 0)
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBox or ComboBox or Slider)
        {
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var step = (ctrl, shift) switch
        {
            (true, true) => 20,
            (true, false) => 10,
            (false, true) => 5,
            (false, false) => 1,
        };

        var canvasWidth = (int)DesignerRoot.Width;
        var canvasHeight = (int)DesignerRoot.Height;
        var newX = Math.Clamp(_selectedItem.X + (dx * step), 0, Math.Max(0, canvasWidth - _selectedItem.Width));
        var newY = Math.Clamp(_selectedItem.Y + (dy * step), 0, Math.Max(0, canvasHeight - _selectedItem.Height));

        _selectedItem.X = newX;
        _selectedItem.Y = newY;
        overlay.SyncPosition();
        OnItemChanged();
        e.Handled = true;
    }

    private void OnAddClockClick(object sender, RoutedEventArgs e) => AddWidget("clock");

    private void OnAddTextClick(object sender, RoutedEventArgs e) => AddWidget("text");

    private void OnAddStatClick(object sender, RoutedEventArgs e) => AddWidget("stat");

    private void OnAddGaugeClick(object sender, RoutedEventArgs e) => AddWidget("gauge");

    private void AddWidget(string type)
    {
        if (_selectedDevice is null)
        {
            return;
        }

        var nextZOrder = _items.Count == 0 ? 0 : _items.Max(i => i.ZOrder) + 1;
        var item = WidgetCatalog.CreateDefault(type, _selectedDevice.ScreenWidth, _selectedDevice.ScreenHeight, nextZOrder);
        _items.Add(item);
        AddOverlayFor(item);

        SaveButton.IsEnabled = true;
        SelectItem(item);
        PreviewElement.InvalidateVisual();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        OverlayCanvas.Children.Remove(_overlays[_selectedItem]);
        _overlays.Remove(_selectedItem);
        _items.Remove(_selectedItem);
        _selectedItem = null;

        SaveButton.IsEnabled = true;
        ShowEmptyPropertyPanel();
        PreviewElement.InvalidateVisual();
    }

    private void LoadBackgroundImage(string? path)
    {
        _backgroundImage?.Dispose();
        _backgroundImage = null;
        _backgroundImagePath = path;

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            _backgroundImage = SKBitmap.Decode(path);
            if (_backgroundImage is null)
            {
                SetStatus($"Could not read '{path}' as an image - background left blank.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Could not read '{path}': {ex.Message}");
        }
    }

    private void OnBackgroundClick(object sender, RoutedEventArgs e)
    {
        if (_selectedDevice is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a background image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        LoadBackgroundImage(dialog.FileName);
        SaveButton.IsEnabled = true;
        PreviewElement.InvalidateVisual();
    }

    private void OnClearBackgroundClick(object sender, RoutedEventArgs e)
    {
        if (_backgroundImagePath is null)
        {
            return;
        }

        LoadBackgroundImage(null);
        SaveButton.IsEnabled = true;
        PreviewElement.InvalidateVisual();
    }

    private void OnShowGridChanged(object sender, RoutedEventArgs e)
    {
        _showGrid = ShowGridCheckBox.IsChecked == true;
        PreviewElement.InvalidateVisual();
    }

    private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        BrightnessValueText.Text = ((int)BrightnessSlider.Value).ToString(CultureInfo.InvariantCulture);

        if (_isLoadingDevice)
        {
            return;
        }

        SaveButton.IsEnabled = true;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_selectedDevice is null)
        {
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            var freshConfig = await _configService.LoadAsync();
            var index = freshConfig.Devices.FindIndex(d => d.Id == _selectedDevice.Id);
            if (index < 0)
            {
                SetStatus("This device is no longer in the config - it may have been removed. Nothing was saved.");
                return;
            }

            var updatedDevice = freshConfig.Devices[index] with
            {
                Widgets = [.. _items.Select(i => i.ToConfig())],
                BackgroundImagePath = _backgroundImagePath,
                Brightness = (int)BrightnessSlider.Value,
            };
            var updatedDevices = new List<DeviceProfileConfig>(freshConfig.Devices);
            updatedDevices[index] = updatedDevice;
            var updatedConfig = freshConfig with { Devices = updatedDevices };

            await _configService.SaveAsync(updatedConfig);

            _config = updatedConfig;
            _selectedDevice = updatedDevice;
            SetStatus("Saved. The display picks up changes within a few seconds.");
        }
        catch (Exception ex)
        {
            SetStatus("Save failed: " + ex.Message);
            SaveButton.IsEnabled = true;
        }
    }

    private void ShowEmptyPropertyPanel()
    {
        PropertyPanel.Children.Clear();
        PropertyPanel.Children.Add(new TextBlock
        {
            Text = "Select a widget to edit its properties, or add a new one above.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
        });
        _positionText = null;
        DeleteButton.IsEnabled = false;
    }

    private void BuildPropertyPanel(WidgetDesignItem item)
    {
        PropertyPanel.Children.Clear();

        var typeDisplayName = WidgetCatalog.Types.FirstOrDefault(t => t.Type == item.Type)?.DisplayName ?? item.Type;
        PropertyPanel.Children.Add(new TextBlock
        {
            Text = typeDisplayName,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 4),
        });

        _positionText = new TextBlock { Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        PropertyPanel.Children.Add(_positionText);
        RefreshPositionText(item);

        switch (item.Type)
        {
            case "clock":
                AddComboBoxField("Time format", WidgetCatalog.TimeFormats, item.GetSetting("timeFormat") ?? WidgetCatalog.DefaultTimeFormat, value =>
                {
                    item.SetSetting("timeFormat", value);
                    OnItemChanged();
                });
                AddColorField(item, "Text color", "textColor");
                AddFontField(item);
                break;

            case "text":
                AddTextField("Text", item.GetSetting("text") ?? string.Empty, value =>
                {
                    item.SetSetting("text", value);
                    OnItemChanged();
                });
                AddColorField(item, "Text color", "textColor");
                AddFontField(item);
                break;

            case "stat":
                AddStatFields(item);
                AddColorField(item, "Text color", "textColor");
                AddFontField(item);
                break;

            case "gauge":
                AddGaugeFields(item);
                AddColorField(item, "Gauge color", "gaugeColor");
                AddColorField(item, "Text color", "textColor");
                AddFontField(item, sizeLabel: "Label text size");
                break;
        }
    }

    private void RefreshPositionText(WidgetDesignItem item)
    {
        if (_positionText is not null)
        {
            _positionText.Text = $"Position {item.X}, {item.Y}   Size {item.Width} x {item.Height}";
        }
    }

    private void AddTextField(string label, string initialValue, Action<string> onChanged)
    {
        PropertyPanel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });
        var textBox = new TextBox { Text = initialValue };
        textBox.LostFocus += (_, _) => onChanged(textBox.Text);
        PropertyPanel.Children.Add(textBox);
    }

    private void AddComboBoxField(string label, IReadOnlyList<string> options, string initialValue, Action<string> onChanged)
    {
        PropertyPanel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });
        var comboBox = new ComboBox { ItemsSource = options, SelectedItem = options.Contains(initialValue) ? initialValue : options[0] };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is string selected)
            {
                onChanged(selected);
            }
        };
        PropertyPanel.Children.Add(comboBox);
    }

    private void AddColorField(WidgetDesignItem item, string label, string settingKey)
    {
        PropertyPanel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });

        var currentHex = item.GetSetting(settingKey);
        var currentSwatch = WidgetCatalog.Colors.FirstOrDefault(c => string.Equals(c.Hex, currentHex, StringComparison.OrdinalIgnoreCase))
            ?? WidgetCatalog.Colors[0];

        var comboBox = new ComboBox { ItemsSource = WidgetCatalog.Colors, DisplayMemberPath = "Name", SelectedItem = currentSwatch };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is WidgetCatalog.ColorSwatch swatch)
            {
                item.SetSetting(settingKey, swatch.Hex);
                OnItemChanged();
            }
        };
        PropertyPanel.Children.Add(comboBox);
    }

    private void AddFontField(WidgetDesignItem item, string sizeLabel = "Size")
    {
        PropertyPanel.Children.Add(new TextBlock { Text = "Font", Margin = new Thickness(0, 6, 0, 2) });

        var currentFamily = item.GetSetting("fontFamily");
        var currentSelection = string.IsNullOrEmpty(currentFamily) ? WidgetCatalog.DefaultFontLabel : currentFamily;
        if (!WidgetCatalog.FontFamilies.Contains(currentSelection))
        {
            currentSelection = WidgetCatalog.DefaultFontLabel;
        }

        var comboBox = new ComboBox { ItemsSource = WidgetCatalog.FontFamilies, SelectedItem = currentSelection };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is string selected)
            {
                item.SetSetting("fontFamily", selected == WidgetCatalog.DefaultFontLabel ? null : selected);
                OnItemChanged();
            }
        };
        PropertyPanel.Children.Add(comboBox);

        var stylePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        var boldCheckBox = new CheckBox { Content = "Bold", IsChecked = item.GetBoolSetting("bold", false), Margin = new Thickness(0, 0, 12, 0) };
        boldCheckBox.Checked += (_, _) => { item.SetBoolSetting("bold", true); OnItemChanged(); };
        boldCheckBox.Unchecked += (_, _) => { item.SetBoolSetting("bold", false); OnItemChanged(); };
        stylePanel.Children.Add(boldCheckBox);

        var italicCheckBox = new CheckBox { Content = "Italic", IsChecked = item.GetBoolSetting("italic", false) };
        italicCheckBox.Checked += (_, _) => { item.SetBoolSetting("italic", true); OnItemChanged(); };
        italicCheckBox.Unchecked += (_, _) => { item.SetBoolSetting("italic", false); OnItemChanged(); };
        stylePanel.Children.Add(italicCheckBox);

        PropertyPanel.Children.Add(stylePanel);

        PropertyPanel.Children.Add(new TextBlock { Text = sizeLabel, Margin = new Thickness(0, 6, 0, 2) });

        // A layout saved before pixel sizes existed may carry the old
        // relative "fontScale" instead of "fontSizePx" - it still renders
        // scaled (WidgetFactory keeps reading it) but shows here as Auto
        // until the user picks an explicit size, which replaces it.
        var currentPixels = item.GetDoubleSetting("fontSizePx", 0);
        var currentSizeOption = WidgetCatalog.FontSizes.FirstOrDefault(s => s.Pixels is { } px && Math.Abs(px - currentPixels) < 0.001)
            ?? WidgetCatalog.FontSizes[0];

        var sizeComboBox = new ComboBox { ItemsSource = WidgetCatalog.FontSizes, DisplayMemberPath = "DisplayName", SelectedItem = currentSizeOption };
        sizeComboBox.SelectionChanged += (_, _) =>
        {
            if (sizeComboBox.SelectedItem is WidgetCatalog.FontSizeOption option)
            {
                item.Settings.Remove("fontScale");
                if (option.Pixels is { } pixels)
                {
                    item.SetDoubleSetting("fontSizePx", pixels);
                }
                else
                {
                    item.Settings.Remove("fontSizePx");
                }

                OnItemChanged();
            }
        };
        PropertyPanel.Children.Add(sizeComboBox);
    }

    /// <summary>
    /// The fixed system/network/integration keys plus one entry per
    /// currently-known Proxmox node (CPU/memory/uptime) and guest
    /// (CPU/memory) - read from ProxmoxNodeDirectory/ProxmoxGuestDirectory,
    /// plain in-memory snapshots the pump service keeps current, so opening
    /// this panel never makes a network call of its own.
    /// </summary>
    private List<WidgetCatalog.StatKeyDescriptor> BuildAvailableStatKeys()
    {
        var keys = new List<WidgetCatalog.StatKeyDescriptor>(WidgetCatalog.StatKeys);

        foreach (var node in _proxmoxNodeDirectory.GetSnapshot())
        {
            keys.Add(new WidgetCatalog.StatKeyDescriptor(
                ProxmoxNodeKeys.CpuUsedPercent(node.Node),
                WidgetCatalog.CategoryProxmox,
                $"{node.Node} CPU",
                node.Node,
                "%"));
            keys.Add(new WidgetCatalog.StatKeyDescriptor(
                ProxmoxNodeKeys.MemoryUsedPercent(node.Node),
                WidgetCatalog.CategoryProxmox,
                $"{node.Node} Memory",
                node.Node,
                "%"));
            keys.Add(new WidgetCatalog.StatKeyDescriptor(
                ProxmoxNodeKeys.UptimeDays(node.Node),
                WidgetCatalog.CategoryProxmox,
                $"{node.Node} Uptime",
                node.Node,
                " days"));
        }

        foreach (var guest in _proxmoxGuestDirectory.GetSnapshot())
        {
            keys.Add(new WidgetCatalog.StatKeyDescriptor(
                ProxmoxGuestKeys.CpuUsedPercent(guest.VmId),
                WidgetCatalog.CategoryProxmox,
                $"{guest.Name} CPU",
                guest.Name,
                "%"));
            keys.Add(new WidgetCatalog.StatKeyDescriptor(
                ProxmoxGuestKeys.MemoryUsedPercent(guest.VmId),
                WidgetCatalog.CategoryProxmox,
                $"{guest.Name} Memory",
                guest.Name,
                "%"));
        }

        return keys;
    }

    /// <summary>
    /// The "Reading" dropdown shared by the stat and gauge widgets: every
    /// available data key, grouped under a non-selectable header per
    /// <see cref="WidgetCatalog.StatKeyDescriptor.Category"/> (Local Device,
    /// Network, Pi-hole, UniFi, Proxmox) so the list stays scannable as more
    /// integrations add more readings.
    /// </summary>
    private void AddReadingField(WidgetDesignItem item)
    {
        var availableKeys = BuildAvailableStatKeys();
        var currentKey = item.GetSetting("dataKey");
        var currentDescriptor = availableKeys.FirstOrDefault(k => k.Key == currentKey) ?? availableKeys[0];

        PropertyPanel.Children.Add(new TextBlock { Text = "Reading", Margin = new Thickness(0, 6, 0, 2) });

        var view = new ListCollectionView(availableKeys);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(WidgetCatalog.StatKeyDescriptor.Category)));

        var headerFactory = new FrameworkElementFactory(typeof(TextBlock));
        headerFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        headerFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        headerFactory.SetValue(TextBlock.MarginProperty, new Thickness(6, 6, 0, 2));
        headerFactory.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var groupStyle = new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = headerFactory } };

        var keyComboBox = new ComboBox { ItemsSource = view, DisplayMemberPath = "DisplayName", SelectedItem = currentDescriptor };
        keyComboBox.GroupStyle.Add(groupStyle);

        // ScrollViewer.CanContentScroll is an inherited attached property;
        // ComboBox's default style sets it True (item-by-item "logical"
        // scrolling), which the grouped dropdown's nested ScrollViewer
        // (Theme/ForgeTheme.xaml) picks up ambiently since it doesn't bind
        // the property itself. With group headers now mixed into a 20+ item
        // list, that line-based stepping felt jumpy/rigid under the mouse
        // wheel. False switches it to smooth pixel-based scrolling.
        // Virtualization buys nothing at this list size and only adds more
        // ways for grouping + scrolling to interact oddly, so it's off too.
        ScrollViewer.SetCanContentScroll(keyComboBox, false);
        VirtualizingPanel.SetIsVirtualizing(keyComboBox, false);

        keyComboBox.SelectionChanged += (_, _) =>
        {
            if (keyComboBox.SelectedItem is WidgetCatalog.StatKeyDescriptor descriptor)
            {
                item.SetSetting("dataKey", descriptor.Key);
                OnItemChanged();
            }
        };
        PropertyPanel.Children.Add(keyComboBox);
    }

    private void AddStatFields(WidgetDesignItem item)
    {
        AddReadingField(item);

        AddTextField("Label", item.GetSetting("label") ?? string.Empty, value =>
        {
            item.SetSetting("label", value);
            OnItemChanged();
        });
        AddTextField("Unit", item.GetSetting("unit") ?? string.Empty, value =>
        {
            item.SetSetting("unit", value);
            OnItemChanged();
        });

        AddDecimalsField(item);
    }

    /// <summary>
    /// A compact arc gauge (see <see cref="GaugeWidget"/>): the same
    /// reading/label/unit/decimals fields as a stat widget, plus the value
    /// range the arc sweeps across, its own color independent of the text
    /// color, its own size control separate from the Font section's Size
    /// (which sizes the label, sitting in its own strip below the ring -
    /// the value's size is always clamped to fit inside the ring itself,
    /// see GaugeWidget.DrawValue), and a Label distance control for the
    /// gap between the ring and that strip.
    /// </summary>
    private void AddGaugeFields(WidgetDesignItem item)
    {
        AddReadingField(item);

        AddTextField("Label", item.GetSetting("label") ?? string.Empty, value =>
        {
            item.SetSetting("label", value);
            OnItemChanged();
        });
        AddTextField("Unit", item.GetSetting("unit") ?? string.Empty, value =>
        {
            item.SetSetting("unit", value);
            OnItemChanged();
        });

        AddNumericField("Label distance", item.GetDoubleSetting("labelGapPx", 0), value =>
        {
            item.SetDoubleSetting("labelGapPx", value);
            OnItemChanged();
        });

        AddDecimalsField(item);
        AddValueSizeField(item);

        AddNumericField("Minimum value", item.GetDoubleSetting("minValue", 0), value =>
        {
            item.SetDoubleSetting("minValue", value);
            OnItemChanged();
        });
        AddNumericField("Maximum value", item.GetDoubleSetting("maxValue", 100), value =>
        {
            item.SetDoubleSetting("maxValue", value);
            OnItemChanged();
        });
    }

    /// <summary>
    /// The gauge value's own size control - deliberately separate from the
    /// Font section's Size (which, for a gauge, sizes only the label). Even
    /// a chosen fixed size here is still clamped at render time to fit
    /// inside the ring without touching it.
    /// </summary>
    private void AddValueSizeField(WidgetDesignItem item)
    {
        PropertyPanel.Children.Add(new TextBlock { Text = "Value text size", Margin = new Thickness(0, 6, 0, 2) });

        var currentPixels = item.GetDoubleSetting("valueFontSizePx", 0);
        var currentSizeOption = WidgetCatalog.FontSizes.FirstOrDefault(s => s.Pixels is { } px && Math.Abs(px - currentPixels) < 0.001)
            ?? WidgetCatalog.FontSizes[0];

        var sizeComboBox = new ComboBox { ItemsSource = WidgetCatalog.FontSizes, DisplayMemberPath = "DisplayName", SelectedItem = currentSizeOption };
        sizeComboBox.SelectionChanged += (_, _) =>
        {
            if (sizeComboBox.SelectedItem is WidgetCatalog.FontSizeOption option)
            {
                if (option.Pixels is { } pixels)
                {
                    item.SetDoubleSetting("valueFontSizePx", pixels);
                }
                else
                {
                    item.Settings.Remove("valueFontSizePx");
                }

                OnItemChanged();
            }
        };
        PropertyPanel.Children.Add(sizeComboBox);
    }

    private void AddDecimalsField(WidgetDesignItem item)
    {
        PropertyPanel.Children.Add(new TextBlock { Text = "Decimal places", Margin = new Thickness(0, 6, 0, 2) });
        var decimalsComboBox = new ComboBox { ItemsSource = new[] { 0, 1, 2 }, SelectedItem = item.GetIntSetting("decimals", 0) };
        decimalsComboBox.SelectionChanged += (_, _) =>
        {
            if (decimalsComboBox.SelectedItem is int decimals)
            {
                item.SetIntSetting("decimals", decimals);
                OnItemChanged();
            }
        };
        PropertyPanel.Children.Add(decimalsComboBox);
    }

    private void AddNumericField(string label, double initialValue, Action<double> onChanged)
    {
        PropertyPanel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });
        var textBox = new TextBox { Text = initialValue.ToString(CultureInfo.InvariantCulture) };
        textBox.LostFocus += (_, _) =>
        {
            if (double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                onChanged(parsed);
            }
            else
            {
                textBox.Text = initialValue.ToString(CultureInfo.InvariantCulture);
            }
        };
        PropertyPanel.Children.Add(textBox);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Black);

        if (_compositor is null)
        {
            return;
        }

        var placements = new List<WidgetPlacement>();
        foreach (var item in _items)
        {
            IWidget widget;
            try
            {
                widget = WidgetFactory.Create(item.ToConfig());
            }
            catch (Exception)
            {
                // An in-progress edit (e.g. a stat widget mid-reconfiguration)
                // shouldn't blank the whole preview - just skip that widget.
                continue;
            }

            placements.Add(new WidgetPlacement(widget, item.X, item.Y, item.ZOrder));
        }

        var context = new WidgetRenderContext { Now = DateTimeOffset.Now, Data = _dataProvider.GetSnapshot() };
        using var frame = _compositor.ComposeFrame(placements, context, _backgroundImage);
        canvas.DrawBitmap(frame, 0, 0);

        if (_showGrid)
        {
            DrawGrid(canvas, (int)DesignerRoot.Width, (int)DesignerRoot.Height);
        }
    }

    private static void DrawGrid(SKCanvas canvas, int width, int height, int spacing = 20)
    {
        using var paint = new SKPaint { Color = new SKColor(255, 255, 255, 90), StrokeWidth = 1, IsAntialias = false };

        for (var x = 0; x <= width; x += spacing)
        {
            canvas.DrawLine(x, 0, x, height, paint);
        }

        for (var y = 0; y <= height; y += spacing)
        {
            canvas.DrawLine(0, y, width, y, paint);
        }
    }
}
