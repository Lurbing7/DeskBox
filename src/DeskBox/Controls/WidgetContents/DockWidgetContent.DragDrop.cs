using DeskBox.Helpers;
using DeskBox.Platform;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class DockWidgetContent
{
    private sealed record DropLocation(DockEntry? Target, int Index, string Directory, Button? Button, int Depth);
    private readonly DispatcherTimer _folderHoverTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private (DockEntry Entry, Button Button, int Depth)? _hoverFolder;
    private bool _dragHoverActive;
    private Button? _insertionButton;
    private Brush? _previousBorder;
    private Thickness _previousThickness;
    private DropLocation? _capturedDrop;
    private DateTimeOffset _lastDockDrop;
    private Button? _pressedDockButton;
    private Windows.Foundation.Point _dockDragStart;
    private bool _startingDockDrag;
    private DateTimeOffset _suppressDockClickUntil;
    private bool SuppressDockClick => _startingDockDrag || DateTimeOffset.UtcNow < _suppressDockClickUntil;

    private void AttachEntryDrag(Button button, DockEntry entry, int depth)
    {
        button.CanDrag = !DockVisualLayout.LockItems(Config);
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (DockVisualLayout.LockItems(Config) || !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
            CancelPointerHover();
            _pressedDockButton = button;
            _dockDragStart = e.GetCurrentPoint(button).Position;
        }), handledEventsToo: true);
        button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(StartDockPointerDrag), handledEventsToo: true);
        button.DragStarting += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                if (DockVisualLayout.LockItems(Config)) { e.Cancel = true; return; }
                IStorageItem item = Directory.Exists(entry.Path) ? await StorageFolder.GetFolderFromPathAsync(entry.Path) : await StorageFile.GetFileFromPathAsync(entry.Path);
                e.Data.SetStorageItems(new[] { item });
                e.Data.RequestedOperation = DataPackageOperation.Copy;
                e.AllowedOperations = DataPackageOperation.Copy | DataPackageOperation.Move;
                _dragHoverActive = true;
                App.Log($"[DockDrag] DragStarting depth={depth} folder={entry.IsFolder}");
            }
            catch (Exception ex) { e.Cancel = true; App.Log($"[DockDrag] Prepare failed type={ex.GetType().Name}"); }
            finally { deferral.Complete(); }
        };
        button.DropCompleted += (_, _) => CancelDragHover();
    }

    private async void StartDockPointerDrag(object sender, PointerRoutedEventArgs e)
    {
        if (DockVisualLayout.LockItems(Config) || _startingDockDrag || sender is not Button button || !ReferenceEquals(_pressedDockButton, button)) return;
        var point = e.GetCurrentPoint(button);
        if (!point.Properties.IsLeftButtonPressed) { _pressedDockButton = null; return; }
        double x = point.Position.X - _dockDragStart.X, y = point.Position.Y - _dockDragStart.Y;
        if (x * x + y * y < 25) return;
        _startingDockDrag = true; _suppressDockClickUntil = DateTimeOffset.UtcNow.AddMilliseconds(500);
        e.Handled = true;
        try { await button.StartDragAsync(point); }
        catch (Exception ex) { App.Log($"[DockDrag] Start failed type={ex.GetType().Name}"); SetStatus(T("Dock.ReadFailed")); }
        finally
        {
            _suppressDockClickUntil = DateTimeOffset.UtcNow.AddMilliseconds(350);
            _pressedDockButton = null; _startingDockDrag = false; CancelDragHover();
        }
    }

    private void AttachPanelDrag(FrameworkElement panel, StackPanel row, DockEntry folder, int depth)
    {
        panel.AllowDrop = true;
        panel.DragOver += (_, e) => HandleRoutedDragOver(e, panel);
        panel.DragLeave += (_, _) => CancelDragHover();
        panel.Drop += async (_, e) => await HandleRoutedDropAsync(e, panel);
    }

    private (int X, int Y) RoutedScreenPoint(DragEventArgs e, FrameworkElement element)
    {
        var root = element.XamlRoot?.Content as UIElement ?? element;
        var point = e.GetPosition(root);
        nint window = _folderPanels.FirstOrDefault(p => ReferenceEquals(p.Row.XamlRoot, element.XamlRoot))?.Window.WindowHandle ?? _window;
        Win32Helper.GetWindowRect(window, out var bounds);
        double scale = element.XamlRoot?.RasterizationScale ?? 1;
        return (bounds.Left + (int)Math.Round(point.X * scale), bounds.Top + (int)Math.Round(point.Y * scale));
    }

    internal void HandleRoutedDragOver(DragEventArgs e, FrameworkElement element)
    {
        bool files = e.DataView.Contains(StandardDataFormats.StorageItems);
        e.AcceptedOperation = files ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (files) { var point = RoutedScreenPoint(e, element); ObserveScreenDrag(point.X, point.Y); }
        e.Handled = true;
    }

    internal async Task HandleRoutedDropAsync(DragEventArgs e, FrameworkElement element)
    {
        e.Handled = true;
        var deferral = e.GetDeferral();
        var point = RoutedScreenPoint(e, element);
        var location = ResolveScreenDrop(point.X, point.Y);
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            await ApplyDropAsync(items.Select(item => item.Path).Where(p => !string.IsNullOrEmpty(p)).ToArray(), location);
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
        catch { SetStatus(T("Dock.ReadFailed")); }
        finally { CancelDragHover(); deferral.Complete(); }
    }

    private DropLocation ResolveScreenDrop(int screenX, int screenY)
    {
        StackPanel row = _row;
        string directory = Config.MappedFolderPath!;
        int depth = 0;
        nint window = _window;
        for (int i = _folderPanels.Count - 1; i >= 0; i--)
        {
            var child = _folderPanels[i]; var b = child.Bounds;
            if (screenX < b.X || screenX >= b.X + b.Width || screenY < b.Y || screenY >= b.Y + b.Height) continue;
            row = child.Row; directory = child.Folder.Target; depth = i + 1; window = child.Window.WindowHandle; break;
        }
        if (!Win32Helper.GetWindowRect(window, out var bounds)) return new(null, 0, directory, null, depth);
        double scale = row.XamlRoot?.RasterizationScale ?? 1;
        double x = (screenX - bounds.Left) / scale, y = (screenY - bounds.Top) / scale;
        bool vertical = depth == 0 && _verticalLayout;
        int count = row.Children.OfType<Button>().Count(b => b.DataContext is DockEntry);
        DropLocation At(DockEntry? target, int visualIndex, Button? button) =>
            new(target, DockAxisLayout.StorageInsertion(visualIndex, count, vertical), directory, button, depth);
        int index = 0;
        foreach (var button in row.Children.OfType<Button>())
        {
            if (button.DataContext is not DockEntry entry) continue;
            if (button.Visibility != Visibility.Visible) { index++; continue; }
            var origin = button.TransformToVisual(row.XamlRoot!.Content).TransformPoint(new Windows.Foundation.Point());
            double primary = vertical ? y : x, start = vertical ? origin.Y : origin.X;
            double extent = vertical ? button.ActualHeight : button.ActualWidth;
            if (primary < start) return At(null, index, button);
            if (x >= origin.X && x <= origin.X + button.ActualWidth && y >= origin.Y && y <= origin.Y + button.ActualHeight)
            {
                double fraction = (primary - start) / Math.Max(1, extent);
                bool center = fraction is >= .2 and <= .8;
                return At(center ? entry : null, index + (fraction > .5 ? 1 : 0), button);
            }
            index++;
        }
        return At(null, index, null);
    }

    internal DockEntry? ScreenDropTarget(int x, int y) => ResolveScreenDrop(x, y).Target;
    internal bool IsInternalDockDrag(IReadOnlyList<string> paths) => paths.Count > 0 && paths.All(p => DockShortcutPlacement.IsOwned(Config.MappedFolderPath!, p));
    internal void CaptureScreenDrop(int x, int y) => _capturedDrop = ResolveScreenDrop(x, y);
    internal Task HandleScreenDropAsync(IReadOnlyList<string> paths, int? x, int? y)
    {
        var location = _capturedDrop ?? ResolveScreenDrop(x ?? int.MaxValue, y ?? int.MaxValue);
        _capturedDrop = null;
        return ApplyDropAsync(paths, location);
    }
    private async Task ApplyDropAsync(IReadOnlyList<string> paths, DropLocation location)
    {
        // Native OLE and the routed XAML Drop can both report one release.
        if (_disposed || DateTimeOffset.UtcNow - _lastDockDrop < TimeSpan.FromMilliseconds(250)) return;
        _lastDockDrop = DateTimeOffset.UtcNow;
        App.Log($"[DockDrag] Drop count={paths.Count} depth={location.Depth} index={location.Index} targetFolder={location.Target?.IsFolder} internal={IsInternalDockDrag(paths)}");
        CancelDragHover();
        await ImportNativeDroppedAppsAsync(paths, location.Target, location.Index, location.Directory);
    }

    internal void ObserveScreenDrag(int x, int y)
    {
        if (_disposed) return;
        CancelPointerHover();
        var location = ResolveScreenDrop(x, y);
        _dragHoverActive = true;
        ClearInsertionIndicator();
        if (location.Target is { IsFolder: true } folder && location.Button is { } button)
        {
            if (_hoverFolder is { } current && current.Entry.Path == folder.Path && current.Depth == location.Depth) return;
            _folderHoverTimer.Stop(); _hoverFolder = (folder, button, location.Depth);
            _folderHoverTimer.Tick -= FolderHoverTick; _folderHoverTimer.Tick += FolderHoverTick;
            _folderHoverTimer.Interval = TimeSpan.FromMilliseconds(DockVisualLayout.ExpandDelay(Config));
            _folderHoverTimer.Start();
        }
        else
        {
            _folderHoverTimer.Stop(); _hoverFolder = null;
            if (location.Target is null && location.Depth == 0 && location.Button is { } insertion)
            {
                _insertionButton = insertion; _previousBorder = insertion.BorderBrush; _previousThickness = insertion.BorderThickness;
                insertion.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
                int buttonIndex = CanonicalRootButtons().Where(b => b.DataContext is DockEntry).ToList().IndexOf(insertion);
                bool before = location.Index <= buttonIndex;
                insertion.BorderThickness = _verticalLayout
                    ? (before ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0))
                    : (before ? new Thickness(2, 0, 0, 0) : new Thickness(0, 0, 2, 0));
            }
        }
    }
    private async void FolderHoverTick(object? sender, object e)
    {
        _folderHoverTimer.Stop();
        if (_hoverFolder is not { } hover || _disposed) return;
        try
        {
            bool stillHovering = false;
            await OpenFolder(hover.Entry, hover.Button, hover.Depth, dragHover: true,
                stillValid: () => stillHovering = _dragHoverActive && _hoverFolder is { } current && current.Button == hover.Button && current.Depth == hover.Depth);
            if (stillHovering) _dragHoverActive = true;
        }
        catch { SetStatus(T("Dock.ReadFailed")); }
    }
    private void ClearInsertionIndicator()
    {
        if (_insertionButton is { } button) { button.BorderBrush = _previousBorder; button.BorderThickness = _previousThickness; }
        _insertionButton = null;
    }
    internal void CancelDragHover()
    {
        _folderHoverTimer.Stop(); _hoverFolder = null; _dragHoverActive = false; ClearInsertionIndicator();
    }
}
