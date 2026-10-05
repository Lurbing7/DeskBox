using DeskBox.Helpers;
using DeskBox.Platform;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class DockWidgetContent
{
    private readonly StackPanel _runningRow = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _runningArea = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    private readonly Border _runningDivider = new() { Width = 1, Height = 28, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, Button> _runningButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Button, FrameworkElement> _runningIndicators = [];
    private IReadOnlyList<DockRunningApplication> _runningApplications = [];
    private readonly DispatcherTimer _runningReconcile = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _runningDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly SemaphoreSlim _runningIconGate = new(2);
    private bool _runningTracking, _runningScanning, _runningScanAgain;
    private int _runningVersion;

    private void InitializeRunningTracking()
    {
        _runningReconcile.Tick += (_, _) => _ = RefreshRunningAsync();
        _runningDebounce.Tick += (_, _) => { _runningDebounce.Stop(); _ = RefreshRunningAsync(); };
        _runningArea.Children.Add(_runningDivider); _runningArea.Children.Add(_runningRow);
        UpdateRunningDivider();
    }

    private void ConfigureRunningTracking()
    {
        bool enabled = !_disposed && _window != 0 && DockVisualLayout.ShowRunning(Config);
        if (enabled == _runningTracking) return;
        _runningTracking = enabled; ++_runningVersion;
        if (enabled) { _runningReconcile.Start(); QueueRunningRefresh(); }
        else
        {
            _runningReconcile.Stop(); _runningDebounce.Stop(); _runningScanAgain = false;
            _runningApplications = []; ClearRunningButtons(); UpdateRunningIndicators(); RequestSize();
        }
    }

    private void QueueRunningRefresh()
    {
        if (!_runningTracking || _disposed) return;
        if (!_runningDebounce.IsEnabled) _runningDebounce.Start();
    }

    private async Task RefreshRunningAsync()
    {
        if (!_runningTracking || _disposed) return;
        // Keep drop targets and native menus stable until the interaction ends.
        if (_startingDockDrag || _dragHoverActive || _itemMenuOpen) return;
        if (_runningScanning) { _runningScanAgain = true; return; }
        int version = _runningVersion; _runningScanning = true;
        try
        {
            var snapshot = await (_runtime?.ReadRunningAsync() ?? Task.Run(DockRunningApplications.Read));
            if (_disposed || !_runningTracking || version != _runningVersion || _startingDockDrag || _dragHoverActive || _itemMenuOpen) return;
            if (SameRunningSnapshot(_runningApplications, snapshot)) return;
            _runningApplications = snapshot;
            UpdateAttention();
            App.Log($"[DockRunning] applications={snapshot.Count} windows={snapshot.Sum(app => app.Windows.Count)} pinnedMatches={_items.Count(item => ReferenceEquals(item.Button.Parent, _row) && RunningFor(item.Entry) is not null)}");
        }
        catch (Exception ex) { if (!_disposed) App.Log("[DockRunning] Scan failed type=" + ex.GetType().Name); }
        finally
        {
            _runningScanning = false;
            if (_runningScanAgain) { _runningScanAgain = false; QueueRunningRefresh(); }
        }
    }

    private static bool SameRunningSnapshot(IReadOnlyList<DockRunningApplication> before, IReadOnlyList<DockRunningApplication> after) =>
        before.Count == after.Count && before.Zip(after).All(pair =>
            pair.First.Key == pair.Second.Key && pair.First.Name == pair.Second.Name && pair.First.Windows.SequenceEqual(pair.Second.Windows));

    private DockRunningApplication? RunningFor(DockEntry entry) => entry.IsFolder ? null :
        _runningApplications.FirstOrDefault(app => string.Equals(app.Executable, entry.Target, StringComparison.OrdinalIgnoreCase));

    private void SyncRunningButtons()
    {
        var pinned = _items.Where(item => ReferenceEquals(item.Button.Parent, _row) && !item.Entry.IsFolder)
            .Select(item => item.Entry.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notified = _listener?.Pending.Values.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var wanted = _runningApplications.Where(app => app.Executable is null || (!pinned.Contains(app.Executable) && !notified.Contains(app.Executable))).ToArray();
        var keys = wanted.Select(app => app.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string key in _runningButtons.Keys.Where(key => !keys.Contains(key)).ToArray())
        {
            var button = _runningButtons[key];
            _runningRow.Children.Remove(button); _items.RemoveAll(item => ReferenceEquals(item.Button, button));
            _runningIndicators.Remove(button); _runningButtons.Remove(key);
        }
        foreach (var app in wanted)
        {
            if (_runningButtons.ContainsKey(app.Key)) continue;
            var entry = _folders.Values.SelectMany(items => items).FirstOrDefault(item => !item.IsFolder && string.Equals(item.Target, app.Executable, StringComparison.OrdinalIgnoreCase))
                ?? new DockEntry(app.Executable ?? app.Key, app.Name, app.Executable ?? app.Key, false);
            var button = CreateButton(entry, runningKey: app.Key);
            _runningButtons[app.Key] = button; _runningRow.Children.Add(button);
        }
        _runningArea.Visibility = _runningButtons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _runningDivider.Visibility = _runningButtons.Count > 0 && FixedEntryCount() > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateRunningIndicators();
    }

    private void UpdateRunningIndicators()
    {
        var alive = _items.Select(item => item.Button).ToHashSet();
        foreach (var button in _runningIndicators.Keys.Where(button => !alive.Contains(button)).ToArray()) _runningIndicators.Remove(button);
        foreach (var (entry, _, button) in _items)
            if (_runningIndicators.TryGetValue(button, out var dot))
                dot.Visibility = DockVisualLayout.ShowRunning(Config) && (_runningButtons.ContainsValue(button) || RunningFor(entry) is not null) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddRunningWindowsMenu(MenuFlyout menu, DockEntry entry)
    {
        if (RunningFor(entry) is not { } app) return;
        var windows = new MenuFlyoutSubItem { Text = T("Dock.SwitchWindow"), Icon = new SymbolIcon(Symbol.Switch) };
        AddWindowItems(windows.Items, app);
        menu.Items.Add(windows);
        AddCloseRunningMenu(menu, app);
    }

    private void ShowRunningMenu(string key, FrameworkElement anchor)
    {
        if (_runningApplications.FirstOrDefault(app => app.Key == key) is not { } app) { QueueRunningRefresh(); return; }
        CancelPointerHover();
        var menu = new MenuFlyout(); AddWindowItems(menu.Items, app);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddCloseRunningMenu(menu, app);
        var pin = new MenuFlyoutItem { Text = T("Dock.PinRunning"), Icon = new SymbolIcon(Symbol.Pin),
            IsEnabled = DockRunningApplications.CanPin(app) && !_items.Any(item => ReferenceEquals(item.Button.Parent, _row) && RunningFor(item.Entry)?.Key == app.Key) };
        pin.Click += (_, _) => DispatcherQueue.TryEnqueue(async () => await PinRunningApplication(app));
        menu.Items.Add(pin);
        menu.Opened += (_, _) => _itemMenuOpen = true;
        menu.Closed += (_, _) => _itemMenuOpen = false;
        Show(menu, anchor);
    }

    private void AddCloseRunningMenu(MenuFlyout menu, DockRunningApplication app)
    {
        var close = new MenuFlyoutItem { Text = T(app.Windows.Count == 1 ? "Dock.CloseWindow" : "Dock.CloseWindows"), Icon = new SymbolIcon(Symbol.Cancel) };
        close.Click += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            int requested = app.Windows.Count(DockRunningApplications.RequestClose);
            App.Log($"[DockRunning] Close requested={requested} windows={app.Windows.Count}");
            if (requested == 0 && !_disposed) SetStatus(T("Dock.LaunchFailed"));
            QueueRunningRefresh();
        });
        menu.Items.Add(close);
    }

    private async Task PinRunningApplication(DockRunningApplication app)
    {
        if (_disposed || !DockRunningApplications.CanPin(app)) return;
        try
        {
            string root = Config.MappedFolderPath!;
            var entries = await Task.Run(() => DockDirectory.Read(root));
            if (entries.Any(entry => !entry.IsFolder && string.Equals(entry.Target, app.Executable, StringComparison.OrdinalIgnoreCase))) return;
            // Reuse the original shortcut to retain launch arguments and icon overrides.
            var source = await Task.Run(() => FindRunningShortcut(root, app.Executable!));
            HideFolderPanel();
            await ImportNativeDroppedAppsAsync([source ?? app.Executable!], insertionIndex: entries.Count, allowLockedPlacement: true);
        }
        catch { if (!_disposed) SetStatus(T("Dock.ReadFailed")); }
    }

    private static string? FindRunningShortcut(string directory, string executable)
    {
        foreach (var entry in DockDirectory.Read(directory))
        {
            if (!entry.IsFolder && string.Equals(entry.Target, executable, StringComparison.OrdinalIgnoreCase)) return entry.Path;
            if (Directory.Exists(entry.Path) && (File.GetAttributes(entry.Path) & FileAttributes.ReparsePoint) == 0 &&
                FindRunningShortcut(entry.Path, executable) is { } child) return child;
        }
        return null;
    }

    private void AddWindowItems(IList<MenuFlyoutItemBase> items, DockRunningApplication app)
    {
        foreach (var window in app.Windows)
        {
            var item = new MenuFlyoutItem { Text = window.Title };
            item.Click += async (_, _) => await ActivateRunningWindow(window);
            items.Add(item);
        }
    }

    private async Task ActivateRunning(string key, FrameworkElement anchor)
    {
        if (_runningApplications.FirstOrDefault(app => app.Key == key) is not { } app) { QueueRunningRefresh(); return; }
        if (app.Windows.Count > 1) ShowRunningMenu(key, anchor);
        else if (app.Windows.Count == 1) await ActivateRunningWindow(app.Windows[0]);
    }

    private async Task ActivateOrLaunchPinned(DockEntry entry, FrameworkElement anchor)
    {
        // A click can arrive before the next reconciliation tick after a new window starts.
        if (DockVisualLayout.ShowRunning(Config) && RunningFor(entry) is null)
        {
            _runningApplications = await (_runtime?.ReadRunningAsync(force: true) ?? Task.Run(DockRunningApplications.Read));
            UpdateRunningIndicators();
        }
        if (DockVisualLayout.ShowRunning(Config) && RunningFor(entry) is { Windows.Count: > 0 } app)
        {
            App.Log($"[DockRunning] Pinned click action=activate windows={app.Windows.Count}");
            await ActivateRunning(app.Key, anchor);
            return;
        }
        App.Log("[DockRunning] Pinned click action=launch");
        await Launch(entry);
    }

    private async Task ActivateRunningWindow(DockRunningWindow window)
    {
        HideFolderPanel();
        try
        {
            if (DockRunningApplications.IsCurrent(window) && await DockAttentionListener.ActivateNotifiedWindowAsync(window.Handle, message => App.Log("[DockRunning] " + message))) return;
            if (!_disposed) SetStatus(T("Dock.LaunchFailed"));
        }
        catch { if (!_disposed) SetStatus(T("Dock.LaunchFailed")); }
        finally { QueueRunningRefresh(); }
    }

    private async Task LoadRunningIcon(Image image, FontIcon glyph, string key, string fallback)
    {
        try
        {
            if (_runningApplications.FirstOrDefault(app => app.Key == key) is not { Windows.Count: > 0 } app) return;
            byte[]? bytes;
            await _runningIconGate.WaitAsync();
            try
            {
                if (_disposed) return;
                bytes = await Task.Run(() => DockRunningApplications.ReadIcon(app.Windows[0]) is { } icon ? DockIconNormalizer.Normalize(icon) : null);
            }
            finally { _runningIconGate.Release(); }
            if (_disposed) return;
            if (bytes is null) { if (File.Exists(fallback)) await LoadIcon(image, glyph, fallback); return; }
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using var writer = new Windows.Storage.Streams.DataWriter(stream);
            writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); stream.Seek(0);
            var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            if (!_disposed) { image.Source = bitmap; glyph.Visibility = Visibility.Collapsed; }
        }
        catch { if (!_disposed && File.Exists(fallback)) await LoadIcon(image, glyph, fallback); }
    }

    private void UpdateRunningDivider() => _runningDivider.Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
    private void ClearRunningButtons()
    {
        foreach (var button in _runningButtons.Values)
        {
            _items.RemoveAll(item => ReferenceEquals(item.Button, button)); _runningIndicators.Remove(button);
        }
        _runningButtons.Clear(); _runningRow.Children.Clear(); _runningArea.Visibility = Visibility.Collapsed;
    }
}
