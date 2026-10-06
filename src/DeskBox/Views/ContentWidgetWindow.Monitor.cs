using DeskBox.Controls.WidgetContents;
using DeskBox.Services;
using Windows.Graphics;

namespace DeskBox.Views;

public sealed partial class ContentWidgetWindow
{
    private bool _monitorSizeQueued;
    protected override SizeInt32 GetPhysicalMinimumWindowSize(int x, int y, int width, int height)
    {
        if (CurrentContent is not SystemMonitorWidgetContent monitor || IsCompactBoundsStateActive)
            return base.GetPhysicalMinimumWindowSize(x, y, width, height);
        double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1;
        double chromeWidth = Math.Max(0, RootGrid.ActualWidth - monitor.ActualWidth);
        double chromeHeight = Math.Max(0, RootGrid.ActualHeight - monitor.ActualHeight);
        return new((int)Math.Round((180 + chromeWidth) * scale),
            (int)Math.Round((monitor.NaturalHeight * 180 / monitor.NaturalWidth + chromeHeight) * scale));
    }
    private void QueueMonitorNaturalSize()
    {
        if (_monitorSizeQueued || IsClosing) return;
        _monitorSizeQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _monitorSizeQueued = false;
            if (CurrentContent is not SystemMonitorWidgetContent monitor || IsClosing || IsResizing ||
                IsCompactBoundsStateActive || !CanPersistBoundsChange(true) || SelectedDisplay() is not { } display) return;
            RootGrid.UpdateLayout();
            double scale = display.DpiScale;
            double shellWidth = Math.Max(0, RootGrid.ActualWidth - monitor.ActualWidth);
            double shellHeight = Math.Max(0, RootGrid.ActualHeight - monitor.ActualHeight);
            var work = WidgetMonitorLayout.WorkArea(display);
            double width = Math.Max(180, _config.Width - shellWidth);
            double factor = Math.Min(width / monitor.NaturalWidth, Math.Min(
                Math.Max(1, work.Width / scale - shellWidth - 24) / monitor.NaturalWidth,
                Math.Max(1, work.Height / scale - shellHeight - 24) / monitor.NaturalHeight));
            int position = int.TryParse(_config.Metadata.GetValueOrDefault("MonitorPosition"), out int saved) ? saved : 6;
            var placed = ExpandContentBoundsToHost(WidgetMonitorLayout.Place(work, scale, position,
                monitor.NaturalWidth * factor + shellWidth, monitor.NaturalHeight * factor + shellHeight, 12));
            var current = GetActualWindowBounds();
            if (Math.Abs(current.Width - placed.Width) <= 1 && Math.Abs(current.Height - placed.Height) <= 1) return;
            ApplyWindowBounds(placed.X, placed.Y, placed.Width, placed.Height, persist: true, updateConfig: true);
            App.Log($"[SystemMonitor] Proportional layout width={monitor.NaturalWidth * factor:F1} height={monitor.NaturalHeight * factor:F1} metrics={SystemMonitorSelection.Read(_config.Metadata)}");
        });
    }

    protected override RectInt32 ConstrainInteractiveResizeBounds(RectInt32 bounds)
    {
        if (CurrentContent is not SystemMonitorWidgetContent monitor || IsCompactBoundsStateActive || SelectedDisplay() is not { } display)
            return base.ConstrainInteractiveResizeBounds(bounds);
        double scale = RootGrid.XamlRoot?.RasterizationScale ?? display.DpiScale;
        int chromeWidth = (int)Math.Round(Math.Max(0, RootGrid.ActualWidth - monitor.ActualWidth) * scale);
        int chromeHeight = (int)Math.Round(Math.Max(0, RootGrid.ActualHeight - monitor.ActualHeight) * scale);
        var work = WidgetMonitorLayout.WorkArea(display);
        var result = SystemMonitorAspectLayout.Resize(
            new(InitialWindowPos.X, InitialWindowPos.Y, InitialWindowSize.Width, InitialWindowSize.Height),
            new(bounds.X, bounds.Y, bounds.Width, bounds.Height), new(work.X, work.Y, work.Width, work.Height),
            ResizeDirection, monitor.NaturalWidth / monitor.NaturalHeight, chromeWidth, chromeHeight, (int)Math.Round(180 * scale));
        return new(result.X, result.Y, result.Width, result.Height);
    }
}
