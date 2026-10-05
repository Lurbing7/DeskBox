using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DeskBox.Controls.WidgetContents;

public sealed class SystemMonitorWidgetContent : UserControl, IWidgetContent, IWidgetHostContextMenuSource, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly SettingsService? _settings;
    private readonly SystemMonitorSampler _sampler;
    private readonly StackPanel _panel = new() { Spacing = 10, Margin = new Thickness(10) };
    private readonly Dictionary<string, SystemMonitorMetricIcon> _icons = [];
    private readonly Dictionary<string, (Grid Row, TextBlock Value)> _rows = [];
    private readonly Dictionary<string, ProgressBar> _bars = [];
    private readonly TextBlock _gpuName = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
    private readonly TextBlock _networkName = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private SystemMonitorSnapshot? _latest;
    private bool _visible, _initialized, _disposed, _collapsed, _revealed;
    private DateTimeOffset _visibleSince;
    private string? _lastError;
    private bool _loggedReady;
    private Action<FlyoutBase, FrameworkElement>? _showFlyout;
    public WidgetConfig Config { get; }
    public string WidgetId => Config.Id;
    public WidgetKind WidgetKind => WidgetKind.SystemMonitor;
    public FrameworkElement View => this;
    public event EventHandler<WidgetHostContextMenuOpeningEventArgs>? HostContextMenuOpening;
    public SystemMonitorWidgetContent(WidgetConfig config, LocalizationService localization, SettingsService? settings = null)
    {
        if (config.WidgetKind != WidgetKind.SystemMonitor) throw new ArgumentException("System monitor config required.", nameof(config));
        Config = config; _localization = localization; _settings = settings;
        _sampler = new(SystemMonitorOptions.Read(config));
        Content = new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _sampler.Updated += Updated;
        _sampler.Failed += Failed;
        _localization.LanguageChanged += LanguageChanged;
        if (_settings is not null) _settings.SettingsChanged += SettingsChanged;
        Loaded += (_, _) => UpdateSampling();
        Unloaded += (_, _) => _sampler.Stop();
        RightTapped += (_, e) =>
        {
            var menu = new MenuFlyout();
            var item = new MenuFlyoutItem { Text = T("Monitor.Settings") };
            item.Click += (_, _) => App.Current.ShowSettings("SystemMonitorSettings");
            menu.Items.Add(item);
            HostContextMenuOpening?.Invoke(this, new(menu));
            if (_showFlyout is not null) _showFlyout(menu, this); else menu.ShowAt(this);
            e.Handled = true;
        };
        Build();
    }
    internal void AttachHost(Action<FlyoutBase, FrameworkElement> showFlyout) => _showFlyout = showFlyout;
    private string T(string key) => _localization.T(key);
    private void Build()
    {
        foreach (var border in _panel.Children.OfType<Border>())
            if (border.Child is StackPanel section) section.Children.Clear();
        _panel.Children.Clear(); _rows.Clear(); _bars.Clear(); _icons.Clear();
        foreach (string group in new[] { "CPU", "GPU", "Memory", "Network" })
        {
            var section = new StackPanel { Spacing = 0 };
            if (group is "CPU" or "GPU") section.Children.Add(new TextBlock { Text = group, FontSize = 16, Margin = new Thickness(0, 0, 0, 4) });
            if (group == "GPU") section.Children.Add(_gpuName);
            if (group == "Network") section.Children.Add(_networkName);
            string[] keys = group switch
            {
                "CPU" => ["CpuLoad", "CpuFrequency", "CpuTemperature", "CpuFan", "CpuPower"],
                "GPU" => ["GpuLoad", "GpuFrequency", "GpuTemperature", "GpuFan", "GpuPower"],
                "Memory" => ["Memory", "Vram"],
                _ => ["Upload", "Download", "IP"]
            };
            foreach (string key in keys)
            {
                var row = new Grid { MinHeight = 26 };
                row.ColumnDefinitions.Add(new() { Width = new GridLength(group is "CPU" or "GPU" ? 30 : 0) });
                row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                string labelKey = key.EndsWith("Load") ? "Load" : key.EndsWith("Frequency") ? "Frequency" : key.EndsWith("Temperature") ? "Temperature" : key.EndsWith("Fan") ? "Fan" : key.EndsWith("Power") ? "Power" : key;
                var label = new TextBlock { Text = T("Monitor." + labelKey), FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var value = new TextBlock { Text = "—", FontSize = key == "IP" ? 12 : 14, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, IsTextSelectionEnabled = key == "IP", TextWrapping = key == "IP" ? TextWrapping.Wrap : TextWrapping.NoWrap, MaxWidth = key == "IP" ? 160 : double.PositiveInfinity };
                if (group is "CPU" or "GPU")
                {
                    var icon = new SystemMonitorMetricIcon(labelKey) { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                    _icons[key] = icon; row.Children.Add(icon);
                }
                Grid.SetColumn(label, 1); Grid.SetColumn(value, 2); row.Children.Add(label); row.Children.Add(value);
                _rows[key] = (row, value); section.Children.Add(row);
                AutomationProperties.SetName(row, label.Text);
                if (key is "Memory" or "Vram")
                {
                    var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 3, IsTabStop = false };
                    AutomationProperties.SetName(bar, label.Text); section.Children.Add(bar); _bars[key] = bar;
                }
            }
            if (group == "Network")
            {
                var rates = new Grid { MinHeight = 26 };
                rates.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                rates.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                rates.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                rates.Children.Add(new TextBlock { Text = T("Monitor.Network"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                int column = 1;
                foreach (string key in new[] { "Upload", "Download" })
                {
                    var row = _rows[key].Row;
                    section.Children.Remove(row);
                    row.ColumnDefinitions[1].Width = GridLength.Auto;
                    row.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                    row.Children.OfType<TextBlock>().First().Text = key == "Upload" ? "↑" : "↓";
                    _rows[key].Value.FontSize = 12;
                    Grid.SetColumn(row, column++); rates.Children.Add(row);
                }
                section.Children.Insert(0, rates);
            }
            var surface = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"{ThemeResource CardBackgroundFillColorDefaultBrush}\" CornerRadius=\"4\" Padding=\"12,8\"/>");
            surface.Child = section; _panel.Children.Add(surface);
        }
        _panel.Children.Add(_status);
        ToolTipService.SetToolTip(_panel, T("Monitor.CapabilityHint"));
        if (_latest is not null) Render(_latest); else _status.Text = T("Monitor.Warmup");
    }
    private void Set(string key, string? value)
    {
        var row = _rows[key]; row.Value.Text = value ?? "—";
        row.Row.Visibility = value is not null || _sampler.Options.ShowUnavailable ? Visibility.Visible : Visibility.Collapsed;
        if (value is null) ToolTipService.SetToolTip(row.Row, T("Monitor.Unavailable")); else ToolTipService.SetToolTip(row.Row, null);
    }
    private static string? Percent(double? value) => value is { } number ? $"{number:F0} %" : null;
    private static string? Capacity(ulong? used, ulong? total) => used is { } u ? total is > 0 ? $"{u / 1073741824d:F1} / {total.Value / 1073741824d:F1} GiB" : $"{u / 1073741824d:F1} GiB" : null;
    private static string? Rate(double? value) => value is { } n ? n >= 1048576 ? $"{n / 1048576:F1} MiB/s" : $"{n / 1024:F1} KiB/s" : null;
    private void Render(SystemMonitorSnapshot snapshot)
    {
        if (!_loggedReady && !snapshot.Warmup)
        {
            App.Log($"[SystemMonitor] Live snapshot rendered cpu={snapshot.CpuLoad.HasValue} memory={snapshot.MemoryUsed.HasValue} gpu={snapshot.GpuLoad.HasValue} vram={snapshot.VramUsed.HasValue} network={snapshot.DownloadBytes.HasValue} cpuTemperature={snapshot.Sensors?.CpuTemperature.HasValue} gpuTemperature={snapshot.Sensors?.GpuTemperature.HasValue}");
            _loggedReady = true;
        }
        _latest = snapshot;
        Set("CpuLoad", Percent(snapshot.CpuLoad));
        Set("CpuFrequency", snapshot.CpuMhz is > 0 ? $"{snapshot.CpuMhz:F0} MHz" : null);
        Set("GpuLoad", Percent(snapshot.GpuLoad));
        Set("CpuTemperature", snapshot.Sensors?.CpuTemperature is { } ct ? $"{ct:F0} °C" : null);
        Set("CpuPower", snapshot.Sensors?.CpuPower is { } cp ? $"{cp:F0} W" : null);
        Set("GpuTemperature", snapshot.Sensors?.GpuTemperature is { } gt ? $"{gt:F0} °C" : null);
        Set("GpuPower", snapshot.Sensors?.GpuPower is { } gp ? $"{gp:F0} W" : null);
        Set("GpuFrequency", snapshot.Sensors?.GpuMhz is { } gc ? $"{gc:F0} MHz" : null);
        Set("CpuFan", null); Set("GpuFan", null);
        _icons["CpuLoad"].Update(snapshot.CpuLoad); _icons["GpuLoad"].Update(snapshot.GpuLoad);
        UpdateTemperature("CpuTemperature", snapshot.Sensors?.CpuTemperature, "MonitorCpu", 90, 100);
        UpdateTemperature("GpuTemperature", snapshot.Sensors?.GpuTemperature, "MonitorGpu", 80, 90);
        _icons["CpuFrequency"].Update(snapshot.CpuMhz); _icons["GpuFrequency"].Update(snapshot.Sensors?.GpuMhz);
        _icons["CpuPower"].Update(snapshot.Sensors?.CpuPower); _icons["GpuPower"].Update(snapshot.Sensors?.GpuPower);
        Set("Memory", Capacity(snapshot.MemoryUsed, snapshot.MemoryTotal)); Set("Vram", Capacity(snapshot.VramUsed, snapshot.VramTotal));
        Set("Upload", Rate(snapshot.UploadBytes)); Set("Download", Rate(snapshot.DownloadBytes));
        Set("IP", snapshot.NetworkAddress);
        UpdateBar("Memory", snapshot.MemoryUsed, snapshot.MemoryTotal); UpdateBar("Vram", snapshot.VramUsed, snapshot.VramTotal);
        _gpuName.Text = snapshot.Gpus.FirstOrDefault(d => d.Id == snapshot.GpuId)?.Name ?? T("Monitor.Unavailable");
        _networkName.Text = snapshot.Networks.FirstOrDefault(d => d.Id == snapshot.NetworkId)?.Name ?? T("Monitor.Unavailable");
        ToolTipService.SetToolTip(_gpuName, _gpuName.Text); ToolTipService.SetToolTip(_networkName, _networkName.Text);
        ToolTipService.SetToolTip(_rows["GpuLoad"].Row, _gpuName.Text);
        ToolTipService.SetToolTip(_rows["Upload"].Row, _networkName.Text);
        _status.Text = snapshot.Warmup ? T("Monitor.Warmup") : snapshot.DeviceFallback ? T("Monitor.DeviceFallback") : "";
        _status.Visibility = string.IsNullOrEmpty(_status.Text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void UpdateBar(string key, ulong? used, ulong? total)
    {
        var bar = _bars[key]; bar.Visibility = used.HasValue && total is > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (used.HasValue && total is > 0) bar.Value = Math.Clamp(used.Value / (double)total.Value * 100, 0, 100);
    }
    private void UpdateTemperature(string key, double? temperature, string prefix, double warning, double critical)
    {
        if (double.TryParse(Config.Metadata.GetValueOrDefault(prefix + "Warning"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double w) && w is > 0 and < 150) warning = w;
        if (double.TryParse(Config.Metadata.GetValueOrDefault(prefix + "Critical"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double c) && c > warning && c < 150) critical = c;
        if (critical <= warning) { warning = prefix == "MonitorCpu" ? 90 : 80; critical = prefix == "MonitorCpu" ? 100 : 90; }
        _icons[key].Update(temperature, warning, critical);
        ToolTipService.SetToolTip(_rows[key].Row, temperature.HasValue ? $"{T("Monitor.DisplayThreshold")}\n{T("Monitor.Warning")}: {warning:F0} °C · {T("Monitor.Critical")}: {critical:F0} °C" : T("Monitor.Unavailable"));
    }
    private void Updated(SystemMonitorSnapshot snapshot) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_disposed && _visible && snapshot.Timestamp >= _visibleSince) { Render(snapshot); _lastError = null; }
    });
    private void Failed(Exception error) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_disposed || !_visible) return;
        _status.Text = T("Monitor.ReadFailed");
        _status.Visibility = Visibility.Visible;
        if (_lastError != error.GetType().Name) App.Log("[SystemMonitor] Read failed: " + error.GetType().Name);
        _lastError = error.GetType().Name;
    });
    private void LanguageChanged() => DispatcherQueue.TryEnqueue(() => { if (!_disposed) Build(); });
    private void SettingsChanged()
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(SettingsChanged); return; }
        if (_disposed) return;
        _sampler.Options = SystemMonitorOptions.Read(Config);
        if (_latest is not null) Render(_latest);
    }
    private void UpdateSampling()
    {
        if (_disposed || !_initialized) return;
        if (_visible && _revealed && !_collapsed) { _visibleSince = DateTimeOffset.Now; _sampler.Start(); } else _sampler.Stop();
    }
    public Task InitializeAsync() { _initialized = true; UpdateSampling(); return Task.CompletedTask; }
    public Task RefreshAsync() { SettingsChanged(); return Task.CompletedTask; }
    public void ApplyAppearance() { }
    public void OnActivated() { }
    public void OnDeactivated() { }
    public void OnWindowVisibilityChanged(bool visible) { _visible = visible; if (!visible) _revealed = false; UpdateSampling(); }
    public void OnWindowRevealCompleted() { _revealed = true; UpdateSampling(); }
    public void OnCompactStateChanged(bool collapsed) { _collapsed = collapsed; UpdateSampling(); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _sampler.Dispose();
        _sampler.Updated -= Updated; _sampler.Failed -= Failed;
        _localization.LanguageChanged -= LanguageChanged;
        if (_settings is not null) _settings.SettingsChanged -= SettingsChanged;
        _showFlyout = null;
    }
}
