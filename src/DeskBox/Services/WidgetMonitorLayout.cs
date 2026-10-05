using DeskBox.Models;
using DeskBox.Platform;
using Windows.Graphics;

namespace DeskBox.Services;

internal static class WidgetMonitorLayout
{
    internal static IReadOnlyList<Win32Helper.MonitorWorkAreaInfo> Screens() => Win32Helper.GetMonitorWorkAreaInfos()
        .Where(screen => screen.WorkArea.Right > screen.WorkArea.Left && screen.WorkArea.Bottom > screen.WorkArea.Top)
        .OrderByDescending(screen => screen.IsPrimary).ThenBy(screen => screen.DeviceName, StringComparer.OrdinalIgnoreCase).ToArray();

    internal static string DockMode(WidgetConfig config) => config.Metadata.GetValueOrDefault("DockDisplayMode") is "primary" or "selected" ? config.Metadata["DockDisplayMode"] : "all";

    internal static IReadOnlyList<Win32Helper.MonitorWorkAreaInfo> DockScreens(WidgetConfig config, IReadOnlyList<Win32Helper.MonitorWorkAreaInfo> screens)
    {
        if (screens.Count == 0) return [];
        if (DockMode(config) == "all") return screens;
        return [Select(screens, DockMode(config) == "selected" ? config.Metadata.GetValueOrDefault("DockMonitorDevice") : null)];
    }

    internal static Win32Helper.MonitorWorkAreaInfo Select(IReadOnlyList<Win32Helper.MonitorWorkAreaInfo> screens, string? device) =>
        screens.FirstOrDefault(screen => !string.IsNullOrEmpty(device) && screen.DeviceName.Equals(device, StringComparison.OrdinalIgnoreCase)) is { DeviceName.Length: > 0 } selected
            ? selected : screens.FirstOrDefault(screen => screen.IsPrimary) is { DeviceName.Length: > 0 } primary ? primary : screens[0];

    internal static RectInt32 WorkArea(Win32Helper.MonitorWorkAreaInfo screen) => new(screen.WorkArea.Left, screen.WorkArea.Top, screen.WorkArea.Right - screen.WorkArea.Left, screen.WorkArea.Bottom - screen.WorkArea.Top);

    internal static RectInt32 Place(RectInt32 work, double scale, int position, double widthDip, double heightDip, double marginDip)
    {
        int width = Math.Clamp((int)Math.Ceiling(widthDip * scale), 1, work.Width);
        int height = Math.Clamp((int)Math.Ceiling(heightDip * scale), 1, work.Height);
        int margin = (int)Math.Ceiling(marginDip * scale), index = Math.Clamp(position, 1, 9) - 1;
        int x = (index % 3) switch { 0 => work.X + margin, 1 => work.X + (work.Width - width) / 2, _ => work.X + work.Width - width - margin };
        int y = (index / 3) switch { 0 => work.Y + margin, 1 => work.Y + (work.Height - height) / 2, _ => work.Y + work.Height - height - margin };
        return new(Math.Clamp(x, work.X, work.X + work.Width - width), Math.Clamp(y, work.Y, work.Y + work.Height - height), width, height);
    }
}
