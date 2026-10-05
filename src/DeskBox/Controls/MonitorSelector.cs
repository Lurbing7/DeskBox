using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Controls;

internal static class MonitorSelector
{
    internal static StackPanel Create(WidgetConfig config, bool dock, Action save)
    {
        string T(string key) => App.Current.LocalizationService.T(key);
        var panel = new StackPanel { Spacing = 12 };
        if (dock)
        {
            string[] modes = ["all", "primary", "selected"];
            var mode = new ComboBox { Header = T("Display.Mode"), MinWidth = 220,
                ItemsSource = new[] { T("Display.All"), T("Display.PrimaryOnly"), T("Display.Selected") },
                SelectedIndex = Array.IndexOf(modes, WidgetMonitorLayout.DockMode(config)) };
            panel.Children.Add(mode);
            mode.SelectionChanged += (_, _) =>
            {
                if (mode.SelectedIndex < 0) return;
                config.Metadata["DockDisplayMode"] = modes[mode.SelectedIndex]; save();
                if (panel.Children.LastOrDefault() is ComboBox select) select.IsEnabled = mode.SelectedIndex == 2;
            };
        }
        var screens = WidgetMonitorLayout.Screens();
        string key = dock ? "DockMonitorDevice" : "MonitorDisplayDevice";
        string? selected = config.Metadata.GetValueOrDefault(key);
        var ids = new List<string> { "" };
        var labels = new List<string> { T("Display.PrimaryOnly") };
        foreach (var screen in screens)
        {
            ids.Add(screen.DeviceName);
            labels.Add($"{T("Display.Screen")} {ids.Count - 1} · {screen.Monitor.Right - screen.Monitor.Left} × {screen.Monitor.Bottom - screen.Monitor.Top}" + (screen.IsPrimary ? " · " + T("Display.Primary") : ""));
        }
        if (!string.IsNullOrEmpty(selected) && !ids.Contains(selected, StringComparer.OrdinalIgnoreCase))
        { ids.Add(selected); labels.Add(T("Display.Disconnected")); }
        var combo = new ComboBox { Header = T("Display.Screen"), ItemsSource = labels, MinWidth = 220,
            IsEnabled = !dock || WidgetMonitorLayout.DockMode(config) == "selected",
            SelectedIndex = Math.Max(0, ids.FindIndex(id => id.Equals(selected ?? "", StringComparison.OrdinalIgnoreCase))) };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { config.Metadata[key] = ids[combo.SelectedIndex]; save(); } };
        panel.Children.Add(combo);
        return panel;
    }
}
