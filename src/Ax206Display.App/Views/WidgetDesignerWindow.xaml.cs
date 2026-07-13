using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Ax206Display.App.Views.Designer;
using Ax206Display.Config.Models;
using Ax206Display.Config.Services;
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
    private readonly DispatcherTimer _timer;

    private readonly List<WidgetDesignItem> _items = [];
    private readonly Dictionary<WidgetDesignItem, DesignerWidgetOverlay> _overlays = [];

    private AppConfig _config = new();
    private DeviceProfileConfig? _selectedDevice;
    private WidgetDesignItem? _selectedItem;
    private FrameCompositor? _compositor;
    private TextBlock? _positionText;
    private bool _isDirty;
    private bool _isLoadingDevice;

    public WidgetDesignerWindow(ConfigService configService, IRenderDataProvider dataProvider)
    {
        InitializeComponent();
        _configService = configService;
        _dataProvider = dataProvider;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => PreviewElement.InvalidateVisual();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();

        Loaded += async (_, _) => await LoadConfigAsync();
    }

    private async Task LoadConfigAsync()
    {
        _config = await _configService.LoadAsync();
        DeviceComboBox.ItemsSource = _config.Devices;

        if (_config.Devices.Count == 0)
        {
            SetStatus("No devices found yet. Connect a display, run the app once so it's discovered, then reopen this window.");
            SetToolbarEnabled(false);
            return;
        }

        SetToolbarEnabled(true);
        DeviceComboBox.SelectedIndex = 0;
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
        _isDirty = false;
        SaveButton.IsEnabled = false;

        _items.Clear();
        _items.AddRange(device.Widgets.Select(WidgetDesignItem.FromConfig));

        _compositor = new FrameCompositor(device.ScreenWidth, device.ScreenHeight);
        DesignerRoot.Width = device.ScreenWidth;
        DesignerRoot.Height = device.ScreenHeight;

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

        _isDirty = true;
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

        _isDirty = true;
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

        _isDirty = true;
        SaveButton.IsEnabled = true;
        ShowEmptyPropertyPanel();
        PreviewElement.InvalidateVisual();
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

            var updatedDevice = freshConfig.Devices[index] with { Widgets = [.. _items.Select(i => i.ToConfig())] };
            var updatedDevices = new List<DeviceProfileConfig>(freshConfig.Devices);
            updatedDevices[index] = updatedDevice;
            var updatedConfig = freshConfig with { Devices = updatedDevices };

            await _configService.SaveAsync(updatedConfig);

            _config = updatedConfig;
            _selectedDevice = updatedDevice;
            _isDirty = false;
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
                break;

            case "text":
                AddTextField("Text", item.GetSetting("text") ?? string.Empty, value =>
                {
                    item.SetSetting("text", value);
                    OnItemChanged();
                });
                AddColorField(item);
                break;

            case "stat":
                AddStatFields(item);
                AddColorField(item);
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

    private void AddStatFields(WidgetDesignItem item)
    {
        var currentKey = item.GetSetting("dataKey");
        var currentDescriptor = WidgetCatalog.StatKeys.FirstOrDefault(k => k.Key == currentKey) ?? WidgetCatalog.StatKeys[0];

        PropertyPanel.Children.Add(new TextBlock { Text = "Reading", Margin = new Thickness(0, 6, 0, 2) });
        var keyComboBox = new ComboBox { ItemsSource = WidgetCatalog.StatKeys, DisplayMemberPath = "DisplayName", SelectedItem = currentDescriptor };
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

        if (_compositor is null || _items.Count == 0)
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
        using var frame = _compositor.ComposeFrame(placements, context);
        canvas.DrawBitmap(frame, 0, 0);
    }
}
