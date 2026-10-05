using DeskBox.Models;

namespace DeskBox.Services;

internal static class DockVisualLayout
{
    public const double Spacing = 2;
    public const double HorizontalPadding = 8;
    public static double ItemSpacing(WidgetConfig config) => Number(config, "DockSpacing", Spacing, 0, 12);
    public static double EdgeMargin(WidgetConfig config) => Number(config, "DockEdgeMargin", 12, 0, 64);
    public static int ExpandDelay(WidgetConfig config) => (int)Number(config, "DockExpandDelay", 600, 200, 1500);
    public static int WheelItems(WidgetConfig config) => (int)Number(config, "DockWheelItems", 3, 1, 6);
    public static bool LockItems(WidgetConfig config) => config.Metadata.GetValueOrDefault("DockLockItems") == "true";
    public static bool HoverExpand(WidgetConfig config) => config.Metadata.GetValueOrDefault("DockHoverExpand") == "true";
    private static double Number(WidgetConfig config, string key, double fallback, double minimum, double maximum) =>
        double.TryParse(config.Metadata.GetValueOrDefault(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
            ? Math.Clamp(value, minimum, maximum) : fallback;
    public static int IconSize(WidgetConfig config) => int.TryParse(config.Metadata.GetValueOrDefault("DockIconSize"), out int value) && value is 32 or 40 or 48 ? value : 40;
    public static bool ShowNames(WidgetConfig config) => config.Metadata.GetValueOrDefault("DockShowNames") == "true";
    public static bool ShowMoreButton(WidgetConfig config) => config.Metadata.GetValueOrDefault("DockShowMoreButton") == "true";
    public static bool ShowRunning(WidgetConfig config) => config.Metadata.GetValueOrDefault("DockShowRunning") != "false";
    public static bool AutoSize(WidgetConfig config) => config.Metadata.GetValueOrDefault("DockAutoSize") != "false";
    public static int Position(WidgetConfig config) => int.TryParse(config.Metadata.GetValueOrDefault("DockPosition"), out int value) && value is >= 1 and <= 9 && value != 5 ? value : 2;
    public static double MaximumRatio(WidgetConfig config) => double.TryParse(config.Metadata.GetValueOrDefault("DockMaximumRatio"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? Math.Clamp(value, .3, .95) : .618;
    public static bool Transparent(WidgetConfig config) => config.Metadata.GetValueOrDefault("DockTransparent") != "false";
    public static double ItemSize(WidgetConfig config) => IconSize(config) + 8;
    public static double Height(WidgetConfig config) => ItemSize(config) + 16 + (ShowNames(config) ? 20 : 0);
    public static double Width(WidgetConfig config, int count, int extraButtons) => HorizontalPadding * 2 + Math.Max(1, count) * ItemSize(config) + Math.Max(0, count - 1) * ItemSpacing(config) + extraButtons * 36;
}
