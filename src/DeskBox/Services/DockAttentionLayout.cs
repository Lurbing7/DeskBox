namespace DeskBox.Services;

internal static class DockAttentionLayout
{
    internal static double RowWidth(int count, double itemSize, double spacing = DockVisualLayout.Spacing) => count * itemSize + Math.Max(0, count - 1) * spacing;
    internal static double Width(int attention, int pinned, int commands, double itemSize, double spacing = DockVisualLayout.Spacing, int running = 0) =>
        DockVisualLayout.HorizontalPadding * 2 + RowWidth(attention, itemSize, spacing) + RowWidth(pinned, itemSize, spacing) + RowWidth(running, itemSize, spacing) +
        (attention > 0 && (pinned > 0 || running > 0) ? 17 : 0) + (pinned > 0 && running > 0 ? 17 : 0) + commands * 36;

    internal static double ScaleToFit(double available, int attention, int pinned, int commands, double itemSize, double spacing = DockVisualLayout.Spacing, int running = 0)
    {
        double icons = RowWidth(attention, itemSize, spacing) + RowWidth(pinned, itemSize, spacing) + RowWidth(running, itemSize, spacing);
        if (icons <= 0) return 1;
        double fixedWidth = Width(attention, pinned, commands, itemSize, spacing, running) - icons;
        return Math.Clamp((available - fixedWidth) / icons, 0, 1);
    }
}
