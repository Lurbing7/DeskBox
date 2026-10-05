using System.Diagnostics;
using DeskBox.Contracts;
using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Platform;
using DeskBox.Services;
using DeskBox.FileSafety;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class DockWidgetContent : UserControl, IWidgetContent, IWidgetHostContextMenuSource, IWidgetHostViewportContent, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = DockVisualLayout.Spacing };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Button _statusButton = new() { Content = "!", Visibility = Visibility.Collapsed };
    private readonly Button _overflow = new() { Content = "…", Visibility = Visibility.Collapsed };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollMode = ScrollMode.Disabled };
    private Action<double, double>? _resize;
    private readonly List<(DockEntry Entry, FrameworkElement Badge, Button Button)> _items = [];
    private readonly Dictionary<Button, TextBlock> _openIndicators = [];
    private readonly Dictionary<string, IReadOnlyList<DockEntry>> _folders = new(StringComparer.OrdinalIgnoreCase);
    private DockAttentionListener? _listener;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private Action<FlyoutBase, FrameworkElement>? _showFlyout;
    private Flyout? _popup;
    private int _generation;
    private int _popupGeneration;
    private bool _disposed;
    private bool _itemMenuOpen;
    private nint _window;
    public WidgetConfig Config { get; }
    public string WidgetId => Config.Id;
    public WidgetKind WidgetKind => WidgetKind.Dock;
    public FrameworkElement View => this;
    public event EventHandler<WidgetHostContextMenuOpeningEventArgs>? HostContextMenuOpening;

    public DockWidgetContent(WidgetConfig config, LocalizationService localization)
    {
        Config = config;
        _localization = localization;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (!config.Metadata.ContainsKey("DockVisualVersion"))
        {
            if (WidgetChromeModeNames.GetOverrideMode(config) is WidgetChromeMode.System or WidgetChromeMode.Overlay)
                WidgetChromeModeNames.SetOverrideMode(config, WidgetChromeMode.Hidden);
            config.Metadata["DockVisualVersion"] = "1";
        }
        if (string.IsNullOrWhiteSpace(config.MappedFolderPath))
            config.MappedFolderPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskBox", "Dock");
        var grid = _layout;
        _attentionArea.Children.Add(_attentionRow); _attentionArea.Children.Add(_attentionDivider);
        grid.Children.Add(_attentionArea);
        InitializeRunningTracking();
        Grid.SetColumn(_runningArea, 2); grid.Children.Add(_runningArea);
        ActualThemeChanged += (_, _) => { UpdateAttentionDivider(); UpdateRunningDivider(); };
        UpdateAttentionDivider();
        _row.HorizontalAlignment = HorizontalAlignment.Center;
        _row.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_scroll, 1);
        _scroll.Content = _row; grid.Children.Add(_scroll);
        var commands = _commands;
        foreach (var command in new[] { _overflow, _statusButton })
        {
            command.Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
            command.Width = 32; command.Height = 56; command.Padding = new Thickness(2);
            commands.Children.Add(command);
        }
        Grid.SetColumn(commands, 3); grid.Children.Add(commands); Content = grid;
        ApplyDockOrientation(force: true);
        _overflow.Click += (_, _) => ShowOverflow();
        _statusButton.Click += (_, _) => ShowStatus();
        Loaded += (_, _) => RequestSize();
        KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.F10 && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) { ShowMenu(); e.Handled = true; } };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await RefreshAsync(); };
        RightTapped += (_, e) => { ShowMenu(); e.Handled = true; };
        _localization.LanguageChanged += LanguageChanged;
        _optionsSignature = OptionsSignature();
    }
    private string T(string key) => _localization.T(key);
    internal void AttachHost(nint window, Action<FlyoutBase, FrameworkElement> showFlyout, Action<double, double> resize)
    {
        _showFlyout = showFlyout;
        _resize = resize;
        RequestSize();
        if (_listener is not null) return;
        _window = window;
        _runtime = DockRuntimeSession.Acquire(Config.MappedFolderPath!, window);
        _runtime.DirectoryChanged += SharedDirectoryChanged;
        _listener = _runtime.Listener;
        _listener.Changed += UpdateAttention;
        _listener.WindowsChanged += QueueRunningRefresh;
        ConfigureRunningTracking();
        App.Log($"[Dock] Shell attention listener available={_listener.Available}");
        if (!_listener.Available) SetStatus(T("Dock.ListenerUnavailable"));
    }
    private void Show(FlyoutBase flyout, FrameworkElement anchor)
    {
        if (_showFlyout is not null) _showFlyout(flyout, anchor); else flyout.ShowAt(anchor);
    }
    public async Task InitializeAsync()
    {
        try
        {
            Directory.CreateDirectory(Config.MappedFolderPath!);
            await Task.Run(() => DockShortcutPlacement.Normalize(Config.MappedFolderPath!));
            await RefreshAsync();
        }
        catch { SetStatus(T("Dock.ReadFailed")); }
    }
    private DockRuntimeSession? _runtime;
    private void SharedDirectoryChanged() => DispatcherQueue.TryEnqueue(() => { if (_disposed) return; _debounce.Stop(); _debounce.Start(); });
    private IReadOnlyList<DockEntry> ReadDirectory(string path) => _runtime?.ReadDirectory(path) ?? DockDirectory.Read(path);
    private void LanguageChanged() => DispatcherQueue.TryEnqueue(() => { if (!_disposed) _ = RefreshAsync(); });
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        int generation = ++_generation;
        try
        {
            var entries = await Task.Run(() => ReadDirectory(Config.MappedFolderPath!));
            var byName = entries.ToDictionary(e => System.IO.Path.GetFileName(e.Path), StringComparer.OrdinalIgnoreCase);
            entries = DockManualOrder.Arrange(byName.Keys, Config.Metadata.GetValueOrDefault("DockOrder")).Select(name => byName[name]).ToArray();
            var folders = await Task.Run(() => entries.Where(e => e.IsFolder).ToDictionary(e => e.Path, e => ReadFolder(e.Target), StringComparer.OrdinalIgnoreCase));
            if (_disposed || generation != _generation) return;
            ++_popupGeneration;
            HideFolderPanel(); _popup?.Hide(); _popup = null; ClearAttentionButtons(); ClearRunningButtons(); _runningIndicators.Clear(); _items.Clear(); _openIndicators.Clear(); _row.Children.Clear(); _folders.Clear();
            foreach (var pair in folders) _folders[pair.Key] = pair.Value;
            foreach (var entry in entries) _row.Children.Add(CreateButton(entry));
            ReverseNewRowsForVertical();
            if (entries.Count == 0)
            {
                var add = new Button { Content = new FontIcon { Glyph = "\uE710" }, Width = DockVisualLayout.ItemSize(Config), Height = DockVisualLayout.ItemSize(Config), Style = (Style)Application.Current.Resources["SubtleButtonStyle"] };
                AutomationProperties.SetName(add, T("Dock.OpenDirectory")); ToolTipService.SetToolTip(add, T("Dock.Empty"));
                add.Click += async (_, _) => await OpenDirectory(); _row.Children.Add(add);
            }
            if (entries.Count == 0) SetStatus(T("Dock.Empty"));
            else SetStatus(_listener is { Available: false } ? T("Dock.ListenerUnavailable") : "");
            UpdateAttention();
            App.Log($"[Dock] Content refreshed entries={entries.Count} folders={folders.Count}");
        }
        catch { if (!_disposed && generation == _generation) SetStatus(T("Dock.ReadFailed")); }
    }
    private IReadOnlyList<DockEntry> ReadFolder(string path)
    {
        try { return ReadDirectory(path); } catch { return []; }
    }
    private Button CreateButton(DockEntry entry, bool popup = false, bool attention = false, int depth = 0, string? runningKey = null)
    {
        double size = DockVisualLayout.IconSize(Config);
        bool names = !entry.IsFolder && DockVisualLayout.ShowNames(Config);
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        var glyph = new FontIcon { Glyph = entry.IsFolder ? "\uE8B7" : "\uE8A5", FontSize = size - 4 };
        var icon = new Grid { Height = size, Width = size }; icon.Children.Add(glyph); icon.Children.Add(image);
        var badge = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28)), Stroke = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"], StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        icon.Children.Add(badge);
        var panel = new StackPanel { Spacing = 4, Width = size, Height = size + (names ? 20 : 0) };
        var label = new TextBlock { Text = entry.Name, FontSize = 12, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = size + 12 };
        if (entry.IsFolder)
        {
            var folderLabel = new TextBlock
            {
                Text = entry.Name, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = size - 4, Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
            };
            var overlay = new Border
            {
                Child = folderLabel, Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(2), Padding = new Thickness(2, 0, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0), IsHitTestVisible = false
            };
            icon.Children.Insert(2, overlay);
        }
        panel.Children.Add(icon);
        if (names && !entry.IsFolder) panel.Children.Add(label);
        var artwork = new Viewbox { Child = panel, Width = size, Height = size + (names ? 20 : 0), Stretch = Stretch.Uniform, Tag = names };
        var content = new Grid { Width = size, Height = size + (names ? 20 : 0) + 4, Tag = names };
        content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new() { Height = new GridLength(4) });
        content.Children.Add(artwork);
        var button = new Button { Content = content, MinWidth = 0, MinHeight = 0, Width = DockVisualLayout.ItemSize(Config), Height = DockVisualLayout.ItemSize(Config) + (names ? 20 : 0), Padding = new Thickness(4, 4, 4, 0), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["SubtleButtonStyle"] };
        if (entry.IsFolder)
        {
            var indicator = new TextBlock { Text = "⌃", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -8), Visibility = Visibility.Collapsed };
            icon.Children.Add(indicator); _openIndicators[button] = indicator;
        }
        button.DataContext = entry;
        if (!entry.IsFolder)
        {
            var runningDot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 4, Height = 4,
                Fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            Grid.SetRow(runningDot, 1);
            content.Children.Add(runningDot); _runningIndicators[button] = runningDot;
        }
        button.RightTapped += (_, e) => { e.Handled = true; if (runningKey is not null) ShowRunningMenu(runningKey, button); else ShowEntryMenu(entry, button, attention, depth); };
        button.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.F10 && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            { e.Handled = true; if (runningKey is not null) ShowRunningMenu(runningKey, button); else ShowEntryMenu(entry, button, attention, depth); }
        };
        if (!attention && runningKey is null) AttachEntryDrag(button, entry, depth);
        if (entry.IsFolder && !attention) AttachPointerHover(button, entry, depth);
        AutomationProperties.SetName(button, entry.Name);
        ToolTipService.SetToolTip(button, entry.Name);
        _items.Add((entry, badge, button));
        nint[] attentionWindows = attention ? _listener?.Pending.Where(item => string.Equals(item.Value, entry.Target, StringComparison.OrdinalIgnoreCase)).Select(item => item.Key).ToArray() ?? [] : [];
        button.Click += async (_, _) =>
        {
            if (SuppressDockClick) return;
            CancelPointerHover();
            try
            {
                if (attention) { HideFolderPanel(); await ActivateAttention(entry, attentionWindows); return; }
                if (runningKey is not null) { await ActivateRunning(runningKey, button); return; }
                if (entry.IsFolder) await OpenFolder(entry, button, depth);
                else { HideFolderPanel(); _popup?.Hide(); if (depth == 0 && !popup) await ActivateOrLaunchPinned(entry, button); else await Launch(entry); }
            }
            catch { if (!_disposed) SetStatus(T("Dock.LaunchFailed")); }
        };
        if (runningKey is null) _ = LoadIcon(image, glyph, entry.Path);
        else _ = LoadRunningIcon(image, glyph, runningKey, entry.Path);
        return button;
    }
    private async Task LoadIcon(Image image, FontIcon glyph, string path)
    {
        try
        {
            var source = await IconHelper.GetIconAsync(path, hideShortcutArrowOverlay: true, showImageFilesAsIcons: true, decodePixelWidth: (int)Math.Ceiling(DockVisualLayout.IconSize(Config) * (XamlRoot?.RasterizationScale ?? 1.5)), normalizeDockArtwork: true);
            if (!_disposed && source is not null) { image.Source = source; glyph.Visibility = Visibility.Collapsed; }
        }
        catch { }
    }
    private async Task OpenFolder(DockEntry folder, FrameworkElement anchor, int depth = 0, bool dragHover = false, Func<bool>? stillValid = null)
    {
        if (_folderPanels.Count > depth && _folderPanels[depth].Folder.Path.Equals(folder.Path, StringComparison.OrdinalIgnoreCase))
        {
            if (!dragHover) CloseFolderBranch(depth);
            return;
        }
        if (depth > _folderPanels.Count || _folderPanels.Take(depth).Any(p => p.Folder.Target.Equals(folder.Target, StringComparison.OrdinalIgnoreCase))) { SetStatus(T("Dock.FolderCycle")); return; }
        int request = ++_popupGeneration;
        var entries = await Task.Run(() => ReadDirectory(folder.Target));
        if (_disposed || !anchor.IsLoaded || request != _popupGeneration || depth > _folderPanels.Count || stillValid?.Invoke() == false) return;
        CloseFolderBranch(depth); _popup?.Hide();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = DockVisualLayout.ItemSpacing(Config) };
        foreach (var entry in entries) row.Children.Add(CreateButton(entry, popup: true, depth: depth + 1));
        var panel = new StackPanel();
        AutomationProperties.SetName(panel, folder.Name);
        if (entries.Count == 0) row.Children.Add(new TextBlock { Text = T("Dock.FolderEmpty"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var scroll = new ScrollViewer { Content = row, Height = DockVisualLayout.Height(Config) - 16, MaxWidth = Math.Max(120, PopupMaximumWidth), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled, ZoomMode = ZoomMode.Disabled };
        AttachChildWheelScrolling(scroll);
        panel.Children.Add(scroll);
        _folders[folder.Path] = entries;
        ShowFolderPanel(panel, row, entries.Count, folder, depth, dragHover);
        UpdateAttention();
        App.Log($"[Dock] Child Dock opened depth={depth + 1} entries={entries.Count}");
    }
    private async Task Launch(DockEntry entry)
    {
        await FileService.OpenItemAsync(new WidgetItem { Name = entry.Name, Path = entry.Path, TargetPath = entry.Target, IsShortcut = entry.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) }, _window, allowBrokenShortcutRepair: false);
    }
    private void UpdateAttention()
    {
        if (_disposed) return;
        if (_listener is { Available: false }) SetStatus(T("Dock.ListenerUnavailable"));
        else if (_status.Text == T("Dock.ListenerUnavailable")) SetStatus("");
        SyncAttentionButtons();
        SyncRunningButtons();
        OrderRuntimeRows();
        foreach (var (entry, badge, button) in _items)
        {
            bool attention = _listener?.HasAttention(entry.Target) == true || (entry.IsFolder && _folders.TryGetValue(entry.Path, out var children) && children.Any(child => _listener?.HasAttention(child.Target) == true));
            badge.Visibility = attention ? Visibility.Visible : Visibility.Collapsed;
            if (_openIndicators.TryGetValue(button, out var indicator)) indicator.Visibility = _folderPanels.Any(p => p.Folder.Path == entry.Path) ? Visibility.Visible : Visibility.Collapsed;
            string name = entry.Name + (attention ? " · " + T("Dock.Attention") : "") +
                (RunningFor(entry) is not null || _runningButtons.ContainsValue(button) ? " · " + T("Dock.Running") : "");
            AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name);
        }
        RequestSize();
    }
    private void ShowMenu()
    {
        CancelPointerHover();
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = T("Dock.OpenDirectory") };
        open.Click += async (_, _) => { try { await FileService.OpenItemAsync(new WidgetItem { Name = T("Dock.Title"), Path = Config.MappedFolderPath!, IsFolder = true }, _window); } catch { SetStatus(T("Dock.LaunchFailed")); } };
        var refresh = new MenuFlyoutItem { Text = T("Dock.Refresh") }; refresh.Click += async (_, _) => await RefreshAsync();
        var clear = new MenuFlyoutItem { Text = T("Dock.ClearAttention") }; clear.Click += (_, _) => _listener?.Clear();
        menu.Items.Add(open); menu.Items.Add(refresh); menu.Items.Add(clear);
        menu.Items.Add(CreateInsertMenu(Config.MappedFolderPath!, null));
        var all = new MenuFlyoutItem { Text = T("Dock.AllItems") };
        all.Click += (_, _) => DispatcherQueue.TryEnqueue(() => ShowOverflow(this)); menu.Items.Add(all);
        var backups = new MenuFlyoutItem { Text = T("Dock.OpenRemovedBackup") };
        backups.Click += async (_, _) =>
        {
            try
            {
                string backup = DockShortcutPlacement.GetBackupDirectory(Config.MappedFolderPath!);
                Directory.CreateDirectory(backup);
                await FileService.OpenItemAsync(new WidgetItem { Path = backup, IsFolder = true }, _window);
            }
            catch { SetStatus(T("Dock.ReadFailed")); }
        };
        menu.Items.Add(backups);
        var names = new ToggleMenuFlyoutItem { Text = T("Dock.ShowNames"), IsChecked = DockVisualLayout.ShowNames(Config) };
        names.Click += async (_, _) => { Config.Metadata["DockShowNames"] = names.IsChecked ? "true" : "false"; SaveOptions(); await RefreshAsync(); }; menu.Items.Add(names);
        var sizes = new MenuFlyoutSubItem { Text = T("Dock.IconSize") };
        foreach (int size in new[] { 32, 40, 48 })
        {
            var item = new ToggleMenuFlyoutItem { Text = size.ToString(), IsChecked = DockVisualLayout.IconSize(Config) == size };
            item.Click += async (_, _) => { Config.Metadata["DockIconSize"] = size.ToString(); SaveOptions(); await RefreshAsync(); }; sizes.Items.Add(item);
        }
        menu.Items.Add(sizes);
        var auto = new ToggleMenuFlyoutItem { Text = T("Dock.AutoSize"), IsChecked = DockVisualLayout.AutoSize(Config) };
        auto.Click += (_, _) => { Config.Metadata["DockAutoSize"] = auto.IsChecked ? "true" : "false"; SaveOptions(); RequestSize(); }; menu.Items.Add(auto);
        var placement = new MenuFlyoutItem { Text = T("Dock.Settings.Title") };
        placement.Click += (_, _) => DispatcherQueue.TryEnqueue(ShowPlacementSettings); menu.Items.Add(placement);
        HostContextMenuOpening?.Invoke(this, new(menu)); Show(menu, this);
    }
    private void ShowEntryMenu(DockEntry entry, Button anchor, bool attention, int depth)
    {
        CancelPointerHover();
        CancelDragHover();
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = T("Dock.OpenItem"), Icon = new SymbolIcon(Symbol.OpenFile) };
        open.Click += (_, _) => DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (attention) { HideFolderPanel(); await ActivateAttention(entry); }
                else if (entry.IsFolder) await OpenFolder(entry, anchor, depth);
                else { HideFolderPanel(); if (depth == 0) await ActivateOrLaunchPinned(entry, anchor); else await Launch(entry); }
            }
            catch { if (!_disposed) SetStatus(T("Dock.LaunchFailed")); }
        });
        menu.Items.Add(open);
        if (!attention) AddRunningWindowsMenu(menu, entry);
        if (attention)
        {
            var clear = new MenuFlyoutItem { Text = T("Dock.ClearAttention") };
            clear.Click += (_, _) => _listener?.Clear(entry.Target); menu.Items.Add(clear);
        }
        else
        {
            var location = new MenuFlyoutItem { Text = T("Widget.ShowInExplorer"), Icon = new SymbolIcon(Symbol.Folder) };
            location.Click += (_, _) => { HideFolderPanel(); Win32Helper.ShowInExplorer(entry.Path); };
            menu.Items.Add(location);
            bool owned = DockShortcutPlacement.IsOwned(Config.MappedFolderPath!, entry.Path);
            if (owned)
            {
                var rename = new MenuFlyoutItem { Text = T("Common.Rename"), Icon = new SymbolIcon(Symbol.Edit), IsEnabled = entry.IsFolder || !entry.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) };
                rename.Click += (_, _) => DispatcherQueue.TryEnqueue(() => ShowNameEditor(entry)); menu.Items.Add(rename);
                int? index = depth == 0 ? _row.Children.OfType<Button>().ToList().IndexOf(anchor) : null;
                menu.Items.Add(CreateInsertMenu(System.IO.Path.GetDirectoryName(entry.Path)!, index));
            }
            if (owned)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                var remove = new MenuFlyoutItem { Text = T("Dock.RemoveItem"), Icon = new SymbolIcon(Symbol.Delete) };
                remove.Click += (_, _) => DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        HideFolderPanel();
                        await Task.Run(() => DockShortcutPlacement.Remove(Config.MappedFolderPath!, entry.Path));
                        await RefreshAsync();
                    }
                    catch { if (!_disposed) SetStatus(T("Dock.RemoveFailed")); }
                });
                menu.Items.Add(remove);
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            var properties = new MenuFlyoutItem { Text = T("Common.Properties"), Icon = new SymbolIcon(Symbol.Setting) };
            properties.Click += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                HideFolderPanel();
                try { if (!ShellContextMenuHelper.ShowProperties(_window, entry.Path)) SetStatus(T("Dock.ReadFailed")); }
                catch { if (!_disposed) SetStatus(T("Dock.ReadFailed")); }
            });
            menu.Items.Add(properties);
            var settings = new MenuFlyoutItem { Text = T("Dock.Settings.Title"), Icon = new SymbolIcon(Symbol.Setting) };
            settings.Click += (_, _) => DispatcherQueue.TryEnqueue(ShowPlacementSettings); menu.Items.Add(settings);
        }
        // Child Dock windows stay alive while their native item menu handles input.
        _itemMenuOpen = true;
        menu.Closed += (_, _) => _itemMenuOpen = false;
        try { Show(menu, anchor); }
        catch { _itemMenuOpen = false; throw; }
    }
    internal async Task ImportNativeDroppedAppsAsync(IReadOnlyList<string> paths, DockEntry? target = null, int? insertionIndex = null, string? directory = null, bool allowLockedPlacement = false)
    {
        try
        {
            bool internalMove = paths.Count > 0 && paths.All(path => DockShortcutPlacement.IsOwned(Config.MappedFolderPath!, path));
            if (internalMove && DockVisualLayout.LockItems(Config) && !allowLockedPlacement) return;
            if (target is { IsFolder: false } && !internalMove)
            {
                if (!ShortcutFileLauncher.TryLaunchWithFiles(target.Path, paths)) SetStatus(T("Dock.LaunchFailed"));
                return;
            }
            string root = target is { IsFolder: true } ? target.Target : directory ?? Config.MappedFolderPath!;
            Directory.CreateDirectory(root);
            bool rejected = false;
            var placed = new List<string>();
            string[] oldOrder = CanonicalRootButtons().Select(b => b.DataContext).OfType<DockEntry>().Select(e => System.IO.Path.GetFileName(e.Path)).ToArray();
            await Task.Run(() =>
            {
                string[] sourceDirectories = paths.Where(path => DockShortcutPlacement.IsOwned(Config.MappedFolderPath!, path))
                    .Select(path => System.IO.Path.GetDirectoryName(path)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (string path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        if (DockShortcutPlacement.IsOwned(Config.MappedFolderPath!, path))
                        {
                            placed.Add(DockShortcutPlacement.MoveDirectory(Config.MappedFolderPath!, root, path));
                            continue;
                        }
                        string link = System.IO.Path.Combine(root, System.IO.Path.GetFileName(path.TrimEnd('\\')) + ".lnk");
                        if (!File.Exists(link)) ShortcutHelper.CreateOrUpdateFolderShortcut(link, path, "");
                        placed.Add(DockShortcutPlacement.Place(Config.MappedFolderPath!, root, link));
                        continue;
                    }
                    if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                    {
                        string link = System.IO.Path.Combine(root, System.IO.Path.GetFileNameWithoutExtension(path) + ".lnk");
                        if (File.Exists(link) && !string.Equals(ShortcutHelper.ReadStoredMetadata(link)?.TargetPath, path, StringComparison.OrdinalIgnoreCase))
                            link = System.IO.Path.Combine(root, System.IO.Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N")[..8] + ".lnk");
                        if (!File.Exists(link)) ShortcutHelper.CreateOrUpdateFolderShortcut(link, path, "");
                        placed.Add(DockShortcutPlacement.Place(Config.MappedFolderPath!, root, link));
                        continue;
                    }
                    if ((!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) || !File.Exists(path)) { rejected = true; continue; }
                    placed.Add(DockShortcutPlacement.Place(Config.MappedFolderPath!, root, path));
                }
                DockShortcutPlacement.PruneEmptyDirectories(Config.MappedFolderPath!, sourceDirectories);
            });
            if (string.Equals(root, Config.MappedFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                Config.Metadata["DockOrder"] = DockManualOrder.Insert(oldOrder, placed.Select(System.IO.Path.GetFileName)!, insertionIndex ?? oldOrder.Length);
                SaveOptions();
            }
            _runtime?.InvalidateDirectories();
            await RefreshAsync(); if (rejected) SetStatus(T("Dock.ShortcutsOnly"));
        }
        catch { SetStatus(T("Dock.ReadFailed")); }
    }
    internal double PopupMaximumWidth { get; set; } = 640;
    internal void ReportDropLaunchFailure() => SetStatus(T("Dock.LaunchFailed"));
    internal void FitRootItems(double available)
    {
        _overflow.Visibility = DockVisualLayout.ShowMoreButton(Config) ? Visibility.Visible : Visibility.Collapsed;
        ApplyRootFit(available);
    }
    private void RequestSize()
    {
        if (_disposed) return;
        ApplyDockOrientation();
        MinHeight = _verticalLayout ? 0 : DockAxisLayout.CrossSize(Config);
        MinWidth = _verticalLayout ? DockAxisLayout.CrossSize(Config) : 0;
        foreach (var command in new[] { _overflow, _statusButton }) command.Height = _verticalLayout ? 32 : DockVisualLayout.ItemSize(Config);
        AutomationProperties.SetName(_overflow, T("Widget.Tooltip.More"));
        ToolTipService.SetToolTip(_overflow, T("Widget.Tooltip.More"));
        int count = FixedEntryCount();
        int extra = CommandCount();
        double length = DockAxisLayout.Length(Config, _attentionEntries.Count, count, _runningButtons.Count, extra);
        _resize?.Invoke(_verticalLayout ? DockAxisLayout.CrossSize(Config) : length, _verticalLayout ? length : DockAxisLayout.CrossSize(Config));
    }
    private void SaveOptions() { App.Current.SettingsService.UpdateWidget(Config); App.Current.SettingsService.SaveDebounced(); }
    private async Task OpenDirectory()
    {
        try { await FileService.OpenItemAsync(new WidgetItem { Name = T("Dock.Title"), Path = Config.MappedFolderPath!, IsFolder = true }, _window); }
        catch { SetStatus(T("Dock.LaunchFailed")); }
    }
    private void ShowStatus()
    {
        _popup?.Hide(); HideFolderPanel();
        _popup = new Flyout { Content = new TextBlock { Text = _status.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 }, Placement = FlyoutPlacementMode.Top };
        Show(_popup, _statusButton);
    }
    private int CommandCount() => (DockVisualLayout.ShowMoreButton(Config) ? 1 : 0) + (_statusButton.Visibility == Visibility.Visible ? 1 : 0);
    private void ShowOverflow(FrameworkElement? anchor = null)
    {
        anchor ??= _overflow.Visibility == Visibility.Visible ? _overflow : this;
        var menu = new MenuFlyout();
        foreach (var attention in _attentionEntries.Values.Where(item => item.Button.Visibility != Visibility.Visible))
        {
            var item = new MenuFlyoutItem { Text = attention.Entry.Name + " · " + T("Dock.Attention") };
            item.Click += async (_, _) => { try { await ActivateAttention(attention.Entry); } catch { if (!_disposed) SetStatus(T("Dock.LaunchFailed")); } }; menu.Items.Add(item);
        }
        foreach (var (entry, _, _) in _items.Where(item => ReferenceEquals(item.Button.Parent, _row)))
        {
            var item = new MenuFlyoutItem { Text = entry.Name };
            item.Click += (_, _) => DispatcherQueue.TryEnqueue(async () => { try { if (entry.IsFolder) await OpenFolder(entry, anchor); else { HideFolderPanel(); await ActivateOrLaunchPinned(entry, anchor); } } catch { SetStatus(T("Dock.LaunchFailed")); } });
            menu.Items.Add(item);
        }
        Show(menu, anchor);
    }
    private void SetStatus(string text)
    {
        if (_disposed) return; _status.Text = text;
        _statusButton.Visibility = string.IsNullOrEmpty(text) || text == T("Dock.Empty") ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(_statusButton, text);
        RequestSize();
    }
    public void ApplyAppearance() { UpdateAttentionDivider(); UpdateRunningDivider(); }
    public void OnHostViewportSizeChanged(double width, double height) => RequestSize();
    public void OnCompactStateChanged(bool collapsed) { if (!collapsed) RequestSize(); }
    public void OnActivated() { }
    public void OnDeactivated() { }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_generation; ++_popupGeneration; _debounce.Stop();
        ConfigureRunningTracking();
        CancelDragHover(); HideFolderPanel();
        CancelPointerHover();
        foreach (var host in _folderHosts) host.Destroy();
        _folderHosts.Clear();
        _popup?.Hide();
        if (_listener is not null) { _listener.Changed -= UpdateAttention; _listener.WindowsChanged -= QueueRunningRefresh; }
        if (_runtime is not null) { _runtime.DirectoryChanged -= SharedDirectoryChanged; _runtime.Dispose(); _runtime = null; }
        ClearAttentionButtons(); _items.Clear(); _runningIndicators.Clear(); _openIndicators.Clear(); _folders.Clear(); _resize = null;
        _localization.LanguageChanged -= LanguageChanged; HostContextMenuOpening = null;
    }
}
