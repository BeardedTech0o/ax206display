using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

    public WidgetDesignerWindow(ConfigService configService, IRenderDataProvider dataProvider, ProxmoxGuestDirectory proxmoxGuestDirectory, DisplayManagerHostedService displayManager)
    {
        InitializeComponent();
        _configService = configService;
        _dataProvider = dataProvider;
        _proxmoxGuestDirectory = proxmoxGuestDirectory;
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
        OverlayCanvas.Width = DesignerRoot.Width;
        OverlayCanvas.Height = DesignerRoot.Height;

        foreach (var item in _items)
        {
            AddOverlayFor(item);
        }
    }

    private void AddOverlayFor(WidgetDesignItem item)
    {
        var overlay = new DesignerWidgetOverlay(item, OverlayCanvas, (int)DesignerRoot.Width, (int)DesignerRoot.Height, SelectItem, OnItemChanged);
        _overlays[item] = overlay;
        OverlayCanvas.Children.Add(overlay);
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

    private void OnAddClockClick(object sender, RoutedEventArgs e) => AddWidget("clock");

    private void OnAddTextClick(object sender, RoutedEventArgs e) => AddWidget("text");

    private void OnAddStatClick(object sender, RoutedEventArgs e) => AddWidget("stat");

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
                AddColorField(item);
                AddFontField(item);
                break;

            case "text":
                AddTextField("Text", item.GetSetting("text") ?? string.Empty, value =>
                {
                    item.SetSetting("text", value);
                    OnItemChanged();
                });
                AddColorField(item);
                AddFontField(item);
                break;

            case "stat":
                AddStatFields(item);
                AddColorField(item);
                AddFontField(item);
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

    private void AddColorField(WidgetDesignItem item)
    {
        PropertyPanel.Children.Add(new TextBlock { Text = "Text color", Margin = new Thickness(0, 6, 0, 2) });

        var currentHex = item.GetSetting("textColor");
        var currentSwatch = WidgetCatalog.Colors.FirstOrDefault(c => string.Equals(c.Hex, currentHex, StringComparison.OrdinalIgnoreCase))
            ?? WidgetCatalog.Colors[0];

        var comboBox = new ComboBox { ItemsSource = WidgetCatalog.Colors, DisplayMemberPath = "Name", SelectedItem = currentSwatch };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is WidgetCatalog.ColorSwatch swatch)
            {
                item.SetSetting("textColor", swatch.Hex);
                OnItemChanged();
            }
        };
        PropertyPanel.Children.Add(comboBox);
    }

    private void AddFontField(WidgetDesignItem item)
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

        PropertyPanel.Children.Add(new TextBlock { Text = "Size", Margin = new Thickness(0, 6, 0, 2) });

        var currentScale = item.GetDoubleSetting("fontScale", WidgetCatalog.DefaultFontScale);
        var currentSizeOption = WidgetCatalog.FontSizes.FirstOrDefault(s => Math.Abs(s.Scale - currentScale) < 0.001)
            ?? WidgetCatalog.FontSizes.First(s => s.Scale == WidgetCatalog.DefaultFontScale);

        var sizeComboBox = new ComboBox { ItemsSource = WidgetCatalog.FontSizes, DisplayMemberPath = "DisplayName", SelectedItem = currentSizeOption };
        sizeComboBox.SelectionChanged += (_, _) =>
        {
            if (sizeComboBox.SelectedItem is WidgetCatalog.FontSizeOption option)
            {
                if (option.Scale == WidgetCatalog.DefaultFontScale)
                {
                    item.Settings.Remove("fontScale");
                }
                else
                {
                    item.SetDoubleSetting("fontScale", option.Scale);
                }

                OnItemChanged();
            }
        };
        PropertyPanel.Children.Add(sizeComboBox);
    }

    /// <summary>
    /// The fixed system/network keys plus one entry per currently-known
    /// Proxmox guest (CPU and memory) - read from ProxmoxGuestDirectory, a
    /// plain in-memory snapshot the pump service keeps current, so opening
    /// this panel never makes a network call of its own.
    /// </summary>
    private List<WidgetCatalog.StatKeyDescriptor> BuildAvailableStatKeys()
    {
        var keys = new List<WidgetCatalog.StatKeyDescriptor>(WidgetCatalog.StatKeys);

        foreach (var guest in _proxmoxGuestDirectory.GetSnapshot())
        {
            keys.Add(new WidgetCatalog.StatKeyDescriptor(
                ProxmoxGuestKeys.CpuUsedPercent(guest.VmId),
                $"Proxmox: {guest.Name} CPU",
                guest.Name,
                "%"));
            keys.Add(new WidgetCatalog.StatKeyDescriptor(
                ProxmoxGuestKeys.MemoryUsedPercent(guest.VmId),
                $"Proxmox: {guest.Name} Memory",
                guest.Name,
                "%"));
        }

        return keys;
    }

    private void AddStatFields(WidgetDesignItem item)
    {
        var availableKeys = BuildAvailableStatKeys();
        var currentKey = item.GetSetting("dataKey");
        var currentDescriptor = availableKeys.FirstOrDefault(k => k.Key == currentKey) ?? availableKeys[0];

        PropertyPanel.Children.Add(new TextBlock { Text = "Reading", Margin = new Thickness(0, 6, 0, 2) });
        var keyComboBox = new ComboBox { ItemsSource = availableKeys, DisplayMemberPath = "DisplayName", SelectedItem = currentDescriptor };
        keyComboBox.SelectionChanged += (_, _) =>
        {
            if (keyComboBox.SelectedItem is WidgetCatalog.StatKeyDescriptor descriptor)
            {
                item.SetSetting("dataKey", descriptor.Key);
                OnItemChanged();
            }
        };
        PropertyPanel.Children.Add(keyComboBox);

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
