using DeskBox.Platform;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class DockWidgetContent
{
    private readonly StackPanel _attentionRow = new() { Orientation = Orientation.Horizontal, Spacing = DockVisualLayout.Spacing };
    private readonly StackPanel _attentionArea = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    private readonly Border _attentionDivider = new() { Width = 1, Height = 28, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, (DockEntry Entry, Button Button)> _attentionEntries = new(StringComparer.OrdinalIgnoreCase);
    private int _lastAttentionCount = -1;
    private double _lastRootScale = -1;
    private int _lastRootFitCount = -1;

    private void UpdateAttentionDivider() => _attentionDivider.Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
    private int FixedEntryCount() => _row.Children.OfType<Button>().Count();

    private void SyncAttentionButtons()
    {
        string[] paths = _listener?.Pending.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var current = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in _attentionEntries.Keys.Where(path => !current.Contains(path)).ToArray())
        {
            var old = _attentionEntries[path];
            _attentionRow.Children.Remove(old.Button);
            _items.RemoveAll(item => ReferenceEquals(item.Button, old.Button));
            _attentionEntries.Remove(path);
        }
        foreach (string path in paths)
        {
            if (_attentionEntries.ContainsKey(path)) continue;
            DockEntry entry = _items.FirstOrDefault(item => !item.Entry.IsFolder && string.Equals(item.Entry.Target, path, StringComparison.OrdinalIgnoreCase)).Entry
                ?? _folders.Values.SelectMany(items => items).FirstOrDefault(item => !item.IsFolder && string.Equals(item.Target, path, StringComparison.OrdinalIgnoreCase))
                ?? new DockEntry(path, System.IO.Path.GetFileNameWithoutExtension(path), path, false);
            var button = CreateButton(entry, attention: true);
            _attentionEntries[path] = (entry, button);
            _attentionRow.Children.Add(button);
        }
        _attentionArea.Visibility = paths.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _attentionDivider.Visibility = paths.Length > 0 && (FixedEntryCount() > 0 || _runningButtons.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        double length = _verticalLayout ? ActualHeight : ActualWidth;
        if (length > 0) FitRootItems(length);
        if (_lastAttentionCount != paths.Length)
        {
            _lastAttentionCount = paths.Length;
            App.Log($"[Dock] Attention area applications={paths.Length} pinned={FixedEntryCount()}");
        }
    }

    private async Task ActivateAttention(DockEntry entry, IReadOnlyList<nint>? capturedWindows = null)
    {
        string path = entry.Target;
        var pending = _listener?.Pending.Where(item => string.Equals(item.Value, path, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        foreach (nint window in pending.Select(item => item.Key).Concat(capturedWindows ?? []).Distinct())
            if (string.Equals(DockAttentionListener.ExecutableForWindow(window), path, StringComparison.OrdinalIgnoreCase) && await DockAttentionListener.ActivateNotifiedWindowAsync(window, message => App.Log("[DockAttention] " + message))) { _listener?.Clear(path); ClearLaunchFailure(); return; }
        SetStatus(T("Dock.LaunchFailed"));
    }

    private void ApplyRootFit(double available)
    {
        int fixedCount = FixedEntryCount();
        double factor = DockAxisLayout.Scale(Config, available, _attentionEntries.Count, fixedCount, _runningButtons.Count, CommandCount());
        _row.Spacing = _attentionRow.Spacing = _runningRow.Spacing = DockVisualLayout.ItemSpacing(Config) * factor;
        _attentionDivider.Visibility = _attentionEntries.Count > 0 && (fixedCount > 0 || _runningButtons.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        _runningDivider.Visibility = _runningButtons.Count > 0 && fixedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in _attentionRow.Children.Concat(_row.Children).Concat(_runningRow.Children).OfType<Button>())
        {
            button.Visibility = Visibility.Visible;
            bool names = button.Content is Grid { Tag: true };
            button.Width = DockVisualLayout.ItemSize(Config) * factor;
            // Keep four DIPs below the scaled artwork for the unscaled running dot.
            button.Height = (_verticalLayout ? DockAxisLayout.Slot(Config) : DockVisualLayout.ItemSize(Config) + (names ? 20 : 0)) * factor + 4 * (1 - factor);
            button.Padding = new Thickness(4 * factor, 4 * factor, 4 * factor, 0);
            if (button.Content is Grid content && content.Children.OfType<Viewbox>().FirstOrDefault() is { } artwork)
            {
                content.Width = artwork.Width = DockVisualLayout.IconSize(Config) * factor;
                artwork.Height = (DockVisualLayout.IconSize(Config) + (names ? 20 : 0)) * factor;
                content.Height = artwork.Height + 4;
            }
        }
        if (Math.Abs(_lastRootScale - factor) > .0001 || _lastRootFitCount != fixedCount + _attentionEntries.Count + _runningButtons.Count)
        {
            _lastRootScale = factor; _lastRootFitCount = fixedCount + _attentionEntries.Count + _runningButtons.Count;
            App.Log($"[Dock] Root fit visible={_lastRootFitCount} scale={factor:F4}");
        }
    }

    private void ClearAttentionButtons()
    {
        foreach (var item in _attentionEntries.Values) _items.RemoveAll(candidate => ReferenceEquals(candidate.Button, item.Button));
        _attentionEntries.Clear(); _attentionRow.Children.Clear(); _attentionArea.Visibility = Visibility.Collapsed;
    }
}
