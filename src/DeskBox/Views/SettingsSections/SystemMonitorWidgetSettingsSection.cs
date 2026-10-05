using System.Net.NetworkInformation;
using DeskBox.Models;
using DeskBox.Controls;
using DeskBox.Services;
using DeskBox.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Views.SettingsSections;

public sealed class SystemMonitorWidgetSettingsSection : UserControl
{
    private readonly StackPanel _panel = new() { Spacing = 16, MaxWidth = 760 };
    private int _generation;
    public SystemMonitorWidgetSettingsSection()
    {
        Content = _panel;
        Loaded += (_, _) => _ = LoadAsync();
        Unloaded += (_, _) => _generation++;
    }
    private async Task LoadAsync()
    {
        int generation = ++_generation;
        var settings = App.Current.SettingsService;
        var localization = App.Current.LocalizationService;
        string T(string key) => localization.T(key);
        _panel.Children.Clear();
        _panel.Children.Add(new TextBlock { Text = T("SystemMonitor.Title"), FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _panel.Children.Add(new TextBlock { Text = T("Monitor.CapabilityHint"), TextWrapping = TextWrapping.Wrap });
        WidgetConfig? config = settings.Settings.Widgets.FirstOrDefault(w => w.WidgetKind == WidgetKind.SystemMonitor && !w.IsDisabled);
        if (config is null)
        {
            var create = new Button { Content = T("Monitor.Enable") };
            create.Click += async (_, _) =>
            {
                create.IsEnabled = false;
                try
                {
                    if (App.Current.WidgetManager is { } manager) await manager.CreateOrShowFeatureWidgetAsync(WidgetKind.SystemMonitor);
                    await LoadAsync();
                }
                catch (Exception ex) { App.Log("[SystemMonitor] Enable failed: " + ex.GetType().Name); create.IsEnabled = true; }
            };
            _panel.Children.Add(create); return;
        }
        var options = SystemMonitorOptions.Read(config);
        _panel.Children.Add(MonitorSelector.Create(config, false, () => { settings.UpdateWidget(config); settings.SaveDebounced(); }));
        _panel.Children.Add(new TextBlock { Text = T("Position.Title") });
        int position = int.TryParse(config.Metadata.GetValueOrDefault("MonitorPosition"), out int savedPosition) && savedPosition is >= 1 and <= 9 ? savedPosition : 6;
        _panel.Children.Add(ScreenPositionSelector.Create(position, true, localization, selected =>
        {
            config.Metadata["MonitorPosition"] = selected.ToString();
            config.Metadata["MonitorPositionRevision"] = Guid.NewGuid().ToString("N");
            settings.UpdateWidget(config);
            settings.SaveDebounced();
        }));
        var interval = new ComboBox { Header = T("Monitor.Interval"), ItemsSource = new[] { "1 s", "2 s", "5 s" }, SelectedIndex = options.IntervalSeconds == 5 ? 2 : options.IntervalSeconds == 2 ? 1 : 0, MinWidth = 220 };
        interval.SelectionChanged += (_, _) => { config.Metadata["MonitorInterval"] = new[] { "1", "2", "5" }[Math.Max(0, interval.SelectedIndex)]; settings.SaveDebounced(); };
        _panel.Children.Add(interval);
        var show = new CheckBox { Content = T("Monitor.ShowUnavailable"), IsChecked = options.ShowUnavailable };
        show.Checked += (_, _) => { config.Metadata["MonitorUnavailable"] = "true"; settings.SaveDebounced(); };
        show.Unchecked += (_, _) => { config.Metadata["MonitorUnavailable"] = "false"; settings.SaveDebounced(); };
        _panel.Children.Add(show);
        _panel.Children.Add(new TextBlock { Text = T("Monitor.DisplayThreshold"), TextWrapping = TextWrapping.Wrap });
        AddThreshold("CPU", "MonitorCpuWarning", T("Monitor.Warning"), 90);
        AddThreshold("CPU", "MonitorCpuCritical", T("Monitor.Critical"), 100);
        AddThreshold("GPU", "MonitorGpuWarning", T("Monitor.Warning"), 80);
        AddThreshold("GPU", "MonitorGpuCritical", T("Monitor.Critical"), 90);
        _panel.Children.Add(new TextBlock { Text = T("Monitor.SourceHint"), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        try
        {
            var devices = await Task.Run(() =>
            {
                var gpus = SystemMonitorNative.GetGpus();
                var networks = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                    .Select(n => new MonitorDevice(n.Id, n.Name)).ToList();
                return (gpus, networks);
            });
            if (generation != _generation || !IsLoaded) return;
            AddDevices(T("Monitor.GpuDevice"), "MonitorGpu", options.GpuId, devices.gpus);
            AddDevices(T("Monitor.NetworkDevice"), "MonitorNetwork", options.NetworkId, devices.networks);
        }
        catch { if (generation == _generation) _panel.Children.Add(new TextBlock { Text = T("Monitor.ReadFailed") }); }
        void AddDevices(string title, string key, string selected, List<MonitorDevice> items)
        {
            items.Insert(0, new("", T("Monitor.Auto")));
            var combo = new ComboBox { Header = title, ItemsSource = items, DisplayMemberPath = nameof(MonitorDevice.Name), SelectedIndex = Math.Max(0, items.FindIndex(d => d.Id == selected)), MinWidth = 220, MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is MonitorDevice device) { config.Metadata[key] = device.Id; settings.SaveDebounced(); } };
            _panel.Children.Add(combo);
        }
        void AddThreshold(string device, string key, string label, double fallback)
        {
            double.TryParse(config.Metadata.GetValueOrDefault(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double saved);
            var input = new NumberBox { Header = $"{device} · {label} (°C)", Minimum = 1, Maximum = 149, Value = saved is > 0 and < 150 ? saved : fallback, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            input.ValueChanged += (_, _) => { if (double.IsFinite(input.Value)) { config.Metadata[key] = input.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); settings.SaveDebounced(); } };
            _panel.Children.Add(input);
        }
    }
}
