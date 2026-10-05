using DeskBox.Helpers;
using DeskBox.Controls;
using DeskBox.Platform;
using DeskBox.Services;
using DeskBox.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class DockWidgetContent
{
    private sealed record FolderPanel(DockEntry Folder, StackPanel Row, StackPopoverHostWindow Window, RectInt32 Bounds, int Direction, NativeDropTarget DropTarget);
    private readonly List<FolderPanel> _folderPanels = [];
    private readonly List<StackPopoverHostWindow> _folderHosts = [];
    private bool _folderInteraction;
    private DockOutsideClickObserver? _outsideClicks;
    internal void CloseFolderPanel() => HideFolderPanel();

    private void HideFolderPanel()
    {
        CancelPointerHover();
        ++_popupGeneration; CloseFolderBranch(0);
    }
    private void CloseFolderBranch(int depth)
    {
        CancelDragHover();
        for (int i = _folderPanels.Count - 1; i >= depth; i--)
        {
            var panel = _folderPanels[i];
            foreach (var button in panel.Row.Children.OfType<Button>())
            {
                _items.RemoveAll(item => ReferenceEquals(item.Button, button));
                _openIndicators.Remove(button);
            }
            panel.DropTarget.Dispose(); panel.Window.HidePopover(); _folderPanels.RemoveAt(i);
        }
        if (_folderPanels.Count == 0 && _folderInteraction)
        {
            _outsideClicks?.Dispose(); _outsideClicks = null;
            _folderInteraction = false;
            App.Current.WidgetManager?.EndWidgetInteraction("DockFolder");
        }
        UpdateAttention();
    }

    private void DismissFolderChainWhenOutside()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            nint foreground = Win32Helper.GetForegroundWindow();
            if (_itemMenuOpen || foreground == _window || _folderPanels.Any(p => p.Window.WindowHandle == foreground) || _dragHoverActive) return;
            HideFolderPanel();
        });
    }

    private void ShowFolderPanel(StackPanel panel, StackPanel row, int count, DockEntry folder, int depth, bool dragHover)
    {
        if (!Win32Helper.GetWindowRect(_window, out var owner)) return;
        var work = DisplayArea.GetFromPoint(new PointInt32((owner.Left + owner.Right) / 2, (owner.Top + owner.Bottom) / 2), DisplayAreaFallback.Nearest).WorkArea;
        double scale = XamlRoot?.RasterizationScale ?? 1;
        int width = Math.Clamp((int)Math.Ceiling((count == 0 ? 180 : DockVisualLayout.Width(Config, count, 0)) * scale), 1, Math.Max(1, (int)(work.Width * DockVisualLayout.MaximumRatio(Config))));
        int height = Math.Min(work.Height, (int)Math.Ceiling(DockVisualLayout.Height(Config) * scale));
        int gap = (int)Math.Ceiling(8 * scale);
        var parent = depth == 0 ? new RectInt32(owner.Left, owner.Top, owner.Right - owner.Left, owner.Bottom - owner.Top) : _folderPanels[depth - 1].Bounds;
        int position = DockVisualLayout.Position(Config);
        int preferred = depth == 0 ? position switch { 4 => 2, 6 => -2, 1 or 2 or 3 => 1, _ => -1 } : _folderPanels[depth - 1].Direction;
        RectInt32? placement = null;
        int chosen = preferred;
        foreach (int direction in new[] { preferred, -1, 1, 2, -2 }.Distinct())
        {
            int alignedX = position switch { 1 or 7 => parent.X, 3 or 9 => parent.X + parent.Width - width, _ => parent.X + (parent.Width - width) / 2 };
            int alignedY = position switch { 1 or 3 => parent.Y, 7 or 9 => parent.Y + parent.Height - height, _ => parent.Y + (parent.Height - height) / 2 };
            int candidateX = direction switch { 2 => parent.X + parent.Width + gap, -2 => parent.X - width - gap, _ => Math.Clamp(alignedX, work.X, work.X + work.Width - width) };
            int candidateY = direction switch { 1 => parent.Y + parent.Height + gap, -1 => parent.Y - height - gap, _ => Math.Clamp(alignedY, work.Y, work.Y + work.Height - height) };
            var candidate = new RectInt32(candidateX, candidateY, width, height);
            bool Intersects(RectInt32 other) => candidate.X < other.X + other.Width && candidate.X + candidate.Width > other.X && candidate.Y < other.Y + other.Height && candidate.Y + candidate.Height > other.Y;
            var root = new RectInt32(owner.Left, owner.Top, owner.Right - owner.Left, owner.Bottom - owner.Top);
            if (candidateX < work.X || candidateY < work.Y || candidateX + width > work.X + work.Width || candidateY + height > work.Y + work.Height || Intersects(root) || _folderPanels.Any(p => Intersects(p.Bounds))) continue;
            placement = candidate; chosen = direction; break;
        }
        if (placement is not { } bounds) { SetStatus(T("Dock.NoSpace")); return; }
        if (depth == _folderHosts.Count)
        {
            var host = new StackPopoverHostWindow(_window, activateInitially: false);
            host.DeactivatedByOutsideClick += DismissFolderChainWhenOutside;
            host.EscapeRequested += HideFolderPanel;
            _folderHosts.Add(host);
        }
        var window = _folderHosts[depth];
        panel.Padding = new Thickness(DockVisualLayout.HorizontalPadding, 8, DockVisualLayout.HorizontalPadding, 8);
        panel.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(panel).Properties.IsLeftButtonPressed && IsBlankDockPress(e.OriginalSource)) HideFolderPanel();
        };
        panel.RequestedTheme = ActualTheme;
        panel.Background = new SolidColorBrush(DockVisualLayout.Transparent(Config) ? Microsoft.UI.Colors.Transparent : ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
        if (panel.Children.LastOrDefault() is ScrollViewer scroll) scroll.MaxWidth = Math.Max(1, width / scale - DockVisualLayout.HorizontalPadding * 2);
        AttachPanelDrag(panel, row, folder, depth + 1);
        var dropTarget = new NativeDropTarget(window.WindowHandle, () => false);
        dropTarget.DragEnterEvent += (x, y, hasFiles) => DispatcherQueue.TryEnqueue(() => { if (hasFiles) ObserveScreenDrag(x, y); });
        dropTarget.DragOverEvent += (x, y) => DispatcherQueue.TryEnqueue(() => ObserveScreenDrag(x, y));
        dropTarget.DragLeaveEvent += () => DispatcherQueue.TryEnqueue(CancelDragHover);
        dropTarget.DropEvent += (paths, x, y, temporary, copy) => DispatcherQueue.TryEnqueue(async () =>
        {
            if (temporary) { SetStatus(T("Dock.ShortcutsOnly")); return; }
            await HandleScreenDropAsync(paths, x, y);
        });
        window.UpdateAppearance(new WidgetMaterialBackdropAppearance(SettingsService.WidgetMaterialTypeAcrylic, ActualTheme == ElementTheme.Dark, AccentColorHelper.DefaultAccentColor, DockVisualLayout.Transparent(Config) ? .15 : 1, 1), true);
        window.SetContent(panel); window.PrepareForShow(bounds);
        _folderPanels.Add(new(folder, row, window, bounds, chosen, dropTarget)); dropTarget.Register();
        if (!_folderInteraction) { _folderInteraction = true; App.Current.WidgetManager?.BeginWidgetInteraction("DockFolder"); }
        _outsideClicks ??= new DockOutsideClickObserver(DispatcherQueue, (x, y) =>
        {
            if (_itemMenuOpen || _folderPanels.Count == 0) return;
            bool Inside(nint hwnd) => Win32Helper.GetWindowRect(hwnd, out var rect) && x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
            if (Inside(_window) || _folderPanels.Any(p => Inside(p.Window.WindowHandle))) return;
            HideFolderPanel();
        });
        window.RevealPrepared(bounds, activate: !dragHover);
    }

    internal static DockEntry? DropTarget(object source)
    {
        for (DependencyObject? current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement { DataContext: DockEntry entry }) return entry;
        return null;
    }

    internal static bool IsBlankDockPress(object source)
    {
        for (DependencyObject? current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is Button or ScrollBar) return false;
        return true;
    }

    private void ShowPlacementSettings()
    {
        HideFolderPanel();
        App.Current.ShowDockSettings(Config.Id);
    }
}
