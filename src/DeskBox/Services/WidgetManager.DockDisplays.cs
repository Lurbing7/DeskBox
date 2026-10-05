using DeskBox.Models;
using DeskBox.Views;

namespace DeskBox.Services;

public sealed partial class WidgetManager
{
    private readonly Dictionary<string, ContentWidgetWindow> _dockReplicas = new(StringComparer.OrdinalIgnoreCase);
    private bool _dockDisplayReconcileQueued, _dockDisplayReconciling, _dockDisplayReconcileAgain, _dockDisplaysStopping;

    private void QueueDockDisplayReconcile()
    {
        if (_dockDisplaysStopping || _dockDisplayReconcileQueued) return;
        _dockDisplayReconcileQueued = true;
        App.UiDispatcherQueue.TryEnqueue(async () =>
        {
            _dockDisplayReconcileQueued = false;
            try { await ReconcileDockDisplaysAsync(); }
            catch (Exception ex) { App.Log("[DockDisplays] Reconcile failed type=" + ex.GetType().Name); }
        });
    }

    private async Task ReconcileDockDisplaysAsync()
    {
        if (_dockDisplaysStopping) return;
        if (_dockDisplayReconciling) { _dockDisplayReconcileAgain = true; return; }
        _dockDisplayReconciling = true;
        try
        {
            var screens = WidgetMonitorLayout.Screens();
            if (screens.Count == 0) return;
            var roots = _contentWidgets.Values.Where(window => window.Config.WidgetKind == WidgetKind.Dock && !window.DockIsClosing).Distinct().ToArray();
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                var targets = WidgetMonitorLayout.DockScreens(root.Config, screens);
                root.SetDockDisplay(targets[0].DeviceName);
                foreach (var screen in targets.Skip(1))
                {
                    string key = root.Config.Id + "@dock:" + screen.DeviceName;
                    wanted.Add(key);
                    if (_dockReplicas.TryGetValue(key, out var existing)) { existing.SetDockDisplay(screen.DeviceName); continue; }
                    var plan = CreateSurfaceContentWindowFactory().CreateContentWindowPlan(root.Config);
                    var replica = new ContentWidgetWindow(root.Config, plan.Content, _settingsService, plan.Descriptor, screen.DeviceName, isDockReplica: true);
                    _dockReplicas[key] = replica;
                    _themeService.TrackWindow(replica);
                    _widgetWindowHandles.Add(replica.WindowHandle);
                    // Replica surface identity is transient and cannot claim the persistent widget id.
                    _widgetSurfaces.RegisterActive(new WidgetSurfaceDefinition(key, null, [key], key), replica);
                    replica.Closed += (_, _) =>
                    {
                        _dockReplicas.Remove(key); _widgetWindowHandles.Remove(replica.WindowHandle); UnregisterSurfaceHost(replica);
                    };
                    try
                    {
                        await replica.ContentReadyTask;
                        replica.SetDockDisplay(screen.DeviceName);
                        if (root.Visible) ShowDockReplica(replica, root.DockIsRaised);
                    }
                    catch
                    {
                        replica.CloseWindow();
                        throw;
                    }
                }
            }
            foreach (var (key, replica) in _dockReplicas.ToArray())
                if (!wanted.Contains(key)) replica.CloseWindow();
            foreach (var monitor in _contentWidgets.Values.Where(window => window.Config.WidgetKind == WidgetKind.SystemMonitor)) monitor.RefreshMonitorDisplay();
            App.Log($"[DockDisplays] screens={screens.Count} roots={roots.Length} replicas={_dockReplicas.Count}");
        }
        finally
        {
            _dockDisplayReconciling = false;
            if (_dockDisplayReconcileAgain) { _dockDisplayReconcileAgain = false; QueueDockDisplayReconcile(); }
        }
    }

    private void IncludeDockReplicas(List<IDesktopWidgetWindow> windows)
    {
        var ids = windows.Where(window => window.Config.WidgetKind == WidgetKind.Dock).Select(window => window.Config.Id).ToHashSet();
        foreach (var replica in _dockReplicas.Values.Where(window => ids.Contains(window.Config.Id) && !window.DockIsClosing))
        {
            replica.RestoreBoundsForCurrentTopology();
            if (!replica.Visible) replica.PrepareTrayShowAnimation();
            windows.Add(replica);
        }
    }

    private static void ShowDockReplica(ContentWidgetWindow replica, bool raised)
    {
        replica.PrepareTrayShowAnimation();
        if (raised) replica.ShowPreparedRaisedFromTray(persistVisibility: false);
        else replica.ShowPreparedAtDesktopLayer(persistVisibility: false);
        replica.CompleteTrayShowWithoutAnimation();
    }

    internal void SynchronizeDockReplicaVisibility(ContentWidgetWindow root)
    {
        if (_dockDisplaysStopping || root.IsDockReplica || root.Config.WidgetKind != WidgetKind.Dock) return;
        App.UiDispatcherQueue.TryEnqueue(() =>
        {
            foreach (var replica in _dockReplicas.Values.Where(window => window.Config.Id == root.Config.Id).ToArray())
            {
                if (root.DockIsClosing) { replica.CloseWindow(); continue; }
                if (root.Visible && !replica.Visible) ShowDockReplica(replica, root.DockIsRaised);
                else if (!root.Visible && replica.Visible) replica.HideWindow();
            }
        });
    }

    private void CloseDockReplicas(string? widgetId = null)
    {
        foreach (var replica in _dockReplicas.Values.Where(window => widgetId is null || window.Config.Id == widgetId).ToArray()) replica.CloseWindow();
    }
}
