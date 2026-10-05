using DeskBox.Controls.WidgetContents;
using DeskBox.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DeskBox.Views;

public sealed partial class ContentWidgetWindow
{
    private DispatcherTimer? _dockSizeTimer;
    private double _dockWidth, _dockHeight;
    private string? _lastMonitorPosition;
    private string? _dockMonitorDevice;
    internal bool IsDockReplica { get; }
    internal bool DockIsClosing => IsClosing;
    internal bool DockIsRaised => IsRaisedFromManager;

    private DeskBox.Platform.Win32Helper.MonitorWorkAreaInfo? SelectedDisplay()
    {
        var screens = WidgetMonitorLayout.Screens();
        if (screens.Count == 0) return null;
        return WidgetMonitorLayout.Select(screens, _config.WidgetKind == DeskBox.Models.WidgetKind.Dock
            ? _dockMonitorDevice ?? (_config.Metadata.GetValueOrDefault("DockDisplayMode") == "selected" ? _config.Metadata.GetValueOrDefault("DockMonitorDevice") : null)
            : _config.Metadata.GetValueOrDefault("MonitorDisplayDevice"));
    }

    internal void SetDockDisplay(string device)
    {
        _dockMonitorDevice = device;
        if (CurrentContent is DockWidgetContent dock)
        {
            dock.CloseFolderPanel();
            RestoreBoundsForCurrentTopology();
            QueueDockSize(dock, _dockWidth, _dockHeight);
        }
    }

    internal void RefreshMonitorDisplay() { _lastMonitorPosition = null; ApplyMonitorPositionSelection(); }

    protected override RectInt32 ResolveWidgetBoundsForCurrentState()
    {
        if (IsCompactBoundsStateActive || _config.WidgetKind is not (DeskBox.Models.WidgetKind.Dock or DeskBox.Models.WidgetKind.SystemMonitor) || SelectedDisplay() is not { } display)
            return base.ResolveWidgetBoundsForCurrentState();
        var work = WidgetMonitorLayout.WorkArea(display);
        if (_config.WidgetKind == DeskBox.Models.WidgetKind.Dock)
        {
            bool vertical = DockAxisLayout.Vertical(_config);
            bool auto = DockVisualLayout.AutoSize(_config);
            double limit = (vertical ? work.Height : work.Width) / display.DpiScale * DockVisualLayout.MaximumRatio(_config);
            double natural = vertical ? (_dockHeight > 0 ? _dockHeight : _config.Width) : (_dockWidth > 0 ? _dockWidth : _config.Width);
            double length = auto ? Math.Min(natural, limit) : _config.Width;
            double cross = auto ? DockAxisLayout.CrossSize(_config) : _config.Height;
            return WidgetMonitorLayout.Place(work, display.DpiScale, DockVisualLayout.Position(_config),
                vertical ? cross : length, vertical ? length : cross, DockVisualLayout.EdgeMargin(_config));
        }
        int position = int.TryParse(_config.Metadata.GetValueOrDefault("MonitorPosition"), out int saved) && saved is >= 1 and <= 9 ? saved : 6;
        return ExpandContentBoundsToHost(WidgetMonitorLayout.Place(work, display.DpiScale, position, _config.Width, _config.Height, 12));
    }

    private void ApplyMonitorPositionSelection()
    {
        string selected = _config.Metadata.GetValueOrDefault("MonitorPosition") ?? "6";
        string selection = selected + ":" + _config.Metadata.GetValueOrDefault("MonitorPositionRevision") + ":" + _config.Metadata.GetValueOrDefault("MonitorDisplayDevice");
        if (CurrentContent is not SystemMonitorWidgetContent || selection == _lastMonitorPosition) return;
        _lastMonitorPosition = selection;
        if (!int.TryParse(selected, out int position) || position is < 1 or > 9) return;
        if (SelectedDisplay() is null) return;
        var placed = ResolveWidgetBoundsForCurrentState();
        ApplyWindowBounds(placed.X, placed.Y, placed.Width, placed.Height, persist: false, updateConfig: false);
    }
    private void QueueDockSize(DockWidgetContent dock, double width, double height)
    {
        _dockWidth = width; _dockHeight = height;
        if (_dockSizeTimer is { IsEnabled: true }) return;
        int attempts = 0;
        _dockSizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var timer = _dockSizeTimer;
        timer.Tick += (_, _) =>
        {
            if (IsClosing || !ReferenceEquals(CurrentContent, dock) || ++attempts > 50)
            { timer.Stop(); return; }
            if (!CanPersistBoundsChange(true) || RootGrid.ActualWidth <= 0 || dock.ActualWidth <= 0 || IsCompactBoundsStateActive) return;
            timer.Stop();
            var bounds = GetActualWindowBounds();
            if (SelectedDisplay() is not { } display) return;
            var work = WidgetMonitorLayout.WorkArea(display);
            if (work.Width <= 0 || work.Height <= 0) return;
            double scale = display.DpiScale;
            dock.PopupMaximumWidth = Math.Max(120, work.Width / scale * DockVisualLayout.MaximumRatio(_config) - 32);
            double shellWidth = Math.Max(0, RootGrid.ActualWidth - dock.ActualWidth);
            double shellHeight = Math.Max(0, RootGrid.ActualHeight - dock.ActualHeight);
            bool vertical = DockAxisLayout.Vertical(_config);
            bool auto = DockVisualLayout.AutoSize(_config);
            double available = auto ? (vertical ? work.Height : work.Width) / scale * DockVisualLayout.MaximumRatio(_config)
                : (vertical ? bounds.Height : bounds.Width) / scale;
            dock.FitRootItems(available - (vertical ? shellHeight : shellWidth));
            double widthDip = _dockWidth + shellWidth, heightDip = _dockHeight + shellHeight;
            if (vertical) heightDip = Math.Min(heightDip, available); else widthDip = Math.Min(widthDip, available);
            var placed = WidgetMonitorLayout.Place(work, scale, DockVisualLayout.Position(_config),
                auto ? widthDip : bounds.Width / scale, auto ? heightDip : bounds.Height / scale, DockVisualLayout.EdgeMargin(_config));
            int w = placed.Width, h = placed.Height, x = placed.X, y = placed.Y;
            if (Math.Abs(w - bounds.Width) <= 1 && Math.Abs(h - bounds.Height) <= 1 && x == bounds.X && y == bounds.Y) return;
            dock.CloseFolderPanel();
            ApplyWindowBounds(x, y, w, h, persist: false, updateConfig: false);
            App.Log($"[Dock] Auto size applied widthDip={w / scale:F1} heightDip={h / scale:F1}");
        };
        timer.Start();
    }
}
