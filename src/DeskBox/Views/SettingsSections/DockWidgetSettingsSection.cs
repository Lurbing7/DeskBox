using System.Globalization;
using DeskBox.Controls;
using DeskBox.FileSafety;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Views.SettingsSections;

public sealed class DockWidgetSettingsSection : UserControl
{
    private readonly StackPanel _panel = new() { Spacing = 16, MaxWidth = 760 };
    private string? _selectedId;
    private readonly Dictionary<string, bool> _expandedGroups = [];
    private string T(string key) => App.Current.LocalizationService.T(key);

    public DockWidgetSettingsSection()
    {
        Content = _panel;
        Loaded += (_, _) => { App.Current.LocalizationService.LanguageChanged += LanguageChanged; Render(); };
        Unloaded += (_, _) => App.Current.LocalizationService.LanguageChanged -= LanguageChanged;
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => { if (IsLoaded && Visibility == Visibility.Visible) Render(); });
    }

    public void SelectWidget(string widgetId) { _selectedId = widgetId; Render(); }
    private void LanguageChanged() => DispatcherQueue.TryEnqueue(() => { if (IsLoaded) Render(); });

    private void Render()
    {
        foreach (var group in _panel.Children.OfType<Expander>())
            if (group.Tag is string key) _expandedGroups[key] = group.IsExpanded;
        _panel.Children.Clear();
        _panel.Children.Add(new TextBlock { Text = T("Dock.Settings.Title"), Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });
        _panel.Children.Add(new TextBlock { Text = T("Dock.Settings.Description"), TextWrapping = TextWrapping.Wrap });
        var settings = App.Current.SettingsService;
        var docks = settings.Settings.WidgetLayout.Widgets.Where(w => w.WidgetKind == WidgetKind.Dock && !w.IsDisabled).ToArray();
        if (docks.Length == 0)
        {
            var create = new Button { Content = T("Dock.Settings.Enable") };
            create.Click += async (_, _) =>
            {
                create.IsEnabled = false;
                try
                {
                    if (App.Current.WidgetManager is { } manager) await manager.CreateOrShowFeatureWidgetAsync(WidgetKind.Dock);
                    Render();
                }
                catch (Exception ex) { App.Log("[DockSettings] Enable failed type=" + ex.GetType().Name); create.IsEnabled = true; }
            };
            _panel.Children.Add(create); return;
        }
        int index = Math.Max(0, Array.FindIndex(docks, w => w.Id == _selectedId));
        WidgetConfig config = docks[index];
        _selectedId = config.Id;
        if (docks.Length > 1)
        {
            var select = new ComboBox { Header = T("Dock.Settings.Instance"), ItemsSource = docks.Select(w => w.Name).ToArray(), SelectedIndex = index, MinWidth = 220 };
            select.SelectionChanged += (_, _) => { if (select.SelectedIndex >= 0) { _selectedId = docks[select.SelectedIndex].Id; Render(); } };
            _panel.Children.Add(select);
        }

        void Save() => settings.UpdateWidget(config);
        void Set(string key, string value) { config.Metadata[key] = value; Save(); }
        void Reset(string key) { config.Metadata.Remove(key); Save(); Render(); }

        var content = Group("Dock.Settings.Content", true);
        Toggle(content, "Dock.ShowRunning", "DockShowRunning", DockVisualLayout.ShowRunning(config));
        content.Children.Add(Hint("Dock.ShowRunningHint"));

        var position = Group("Dock.Settings.Position", true);
        position.Children.Add(MonitorSelector.Create(config, true, Save));
        position.Children.Add(new TextBlock { Text = T("Position.Title") });
        position.Children.Add(ScreenPositionSelector.Create(DockVisualLayout.Position(config), false, App.Current.LocalizationService, value => Set("DockPosition", value.ToString(CultureInfo.InvariantCulture))));
        position.Children.Add(ResetButton(() => Reset("DockPosition")));
        Number(position, "Dock.Settings.EdgeMargin", "DockEdgeMargin", DockVisualLayout.EdgeMargin(config), 0, 64, 1);
        Number(position, "Dock.MaximumRatio", "DockMaximumRatio", DockVisualLayout.MaximumRatio(config), .3, .95, .01);
        Toggle(position, "Dock.AutoSize", "DockAutoSize", DockVisualLayout.AutoSize(config));

        var appearance = Group("Dock.Settings.Appearance");
        var size = new ComboBox { Header = T("Dock.IconSize"), ItemsSource = new[] { "32 DIP", "40 DIP", "48 DIP" }, SelectedIndex = Array.IndexOf(new[] { 32, 40, 48 }, DockVisualLayout.IconSize(config)), MinWidth = 220 };
        size.SelectionChanged += (_, _) => { if (size.SelectedIndex >= 0) Set("DockIconSize", new[] { "32", "40", "48" }[size.SelectedIndex]); };
        Row(appearance, size, () => Reset("DockIconSize"));
        Number(appearance, "Dock.Settings.Spacing", "DockSpacing", DockVisualLayout.ItemSpacing(config), 0, 12, 1);
        Toggle(appearance, "Dock.ShowNames", "DockShowNames", DockVisualLayout.ShowNames(config));
        Toggle(appearance, "Dock.Transparent", "DockTransparent", DockVisualLayout.Transparent(config));
        Toggle(appearance, "Dock.ShowMoreButton", "DockShowMoreButton", DockVisualLayout.ShowMoreButton(config));

        var behavior = Group("Dock.Settings.Behavior");
        Toggle(behavior, "Dock.Settings.LockItems", "DockLockItems", DockVisualLayout.LockItems(config));
        behavior.Children.Add(Hint("Dock.Settings.LockItemsHint"));
        var lockPosition = new ToggleSwitch { Header = T("Dock.Settings.LockPosition"), IsOn = config.IsPositionLocked };
        lockPosition.Toggled += (_, _) => { config.IsPositionLocked = lockPosition.IsOn; Save(); };
        Row(behavior, lockPosition, () => { config.IsPositionLocked = false; Save(); Render(); });

        var children = Group("Dock.Settings.Children");
        Number(children, "Dock.Settings.ExpandDelay", "DockExpandDelay", DockVisualLayout.ExpandDelay(config), 200, 1500, 100);
        Toggle(children, "Dock.Settings.HoverExpand", "DockHoverExpand", DockVisualLayout.HoverExpand(config));
        Number(children, "Dock.Settings.WheelItems", "DockWheelItems", DockVisualLayout.WheelItems(config), 1, 6, 1);
        children.Children.Add(Hint("Dock.Settings.ScrollHint"));

        var maintenance = Group("Dock.Settings.Maintenance");
        Link(maintenance, "Dock.OpenDirectory", () => OpenDirectory(config.MappedFolderPath));
        Link(maintenance, "Dock.OpenRemovedBackup", () => OpenDirectory(DockShortcutPlacement.GetBackupDirectory(config.MappedFolderPath!)));
        Link(maintenance, "Settings.DataBackup.Title", () => App.Current.ShowSettings("BackupRestoreSettings"));
        Link(maintenance, "Dock.Settings.Startup", () => App.Current.ShowSettings("General"));
        maintenance.Children.Add(Hint("Dock.Settings.BackupHint"));

        void Number(StackPanel group, string title, string key, double value, double minimum, double maximum, double step)
        {
            var input = new NumberBox { Header = T(title), Value = value, Minimum = minimum, Maximum = maximum, SmallChange = step,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, MinWidth = 220 };
            input.ValueChanged += (_, _) =>
            {
                if (!double.IsFinite(input.Value)) return;
                double selected = Math.Clamp(input.Value, minimum, maximum);
                if (key is "DockExpandDelay" or "DockWheelItems") selected = Math.Round(selected);
                Set(key, selected.ToString(CultureInfo.InvariantCulture));
            };
            Row(group, input, () => Reset(key));
        }
        void Toggle(StackPanel group, string title, string key, bool value)
        {
            var input = new ToggleSwitch { Header = T(title), IsOn = value };
            input.Toggled += (_, _) => Set(key, input.IsOn ? "true" : "false");
            Row(group, input, () => Reset(key));
        }
    }

    private StackPanel Group(string title, bool expanded = false)
    {
        var body = new StackPanel { Spacing = 16, Margin = new Thickness(12) };
        _panel.Children.Add(new Expander { Header = T(title), Tag = title, Content = body, IsExpanded = _expandedGroups.GetValueOrDefault(title, expanded), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        return body;
    }
    private TextBlock Hint(string key) => new() { Text = T(key), TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
    private Button ResetButton(Action reset)
    {
        var button = new Button { Content = new SymbolIcon(Symbol.Refresh), VerticalAlignment = VerticalAlignment.Bottom };
        AutomationProperties.SetName(button, T("Dock.Settings.Reset"));
        ToolTipService.SetToolTip(button, T("Dock.Settings.Reset"));
        button.Click += (_, _) => reset(); return button;
    }
    private void Row(StackPanel group, FrameworkElement input, Action reset)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        row.Children.Add(input);
        var button = ResetButton(reset); Grid.SetColumn(button, 1); row.Children.Add(button);
        group.Children.Add(row);
    }
    private void Link(StackPanel group, string title, Action action)
    {
        var button = new Button { Content = T(title) }; button.Click += (_, _) => action(); group.Children.Add(button);
    }
    private async void OpenDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { Directory.CreateDirectory(path); await FileService.OpenItemAsync(new WidgetItem { Path = path, IsFolder = true }, 0); }
        catch (Exception ex) { App.Log("[DockSettings] Open folder failed type=" + ex.GetType().Name); }
    }
}
