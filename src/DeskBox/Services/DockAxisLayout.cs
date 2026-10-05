using DeskBox.Models;

namespace DeskBox.Services;

internal static class DockAxisLayout
{
    internal static bool Vertical(int position) => position is 4 or 6;
    internal static bool Vertical(WidgetConfig config) => Vertical(DockVisualLayout.Position(config));
    internal static int StorageInsertion(int visualIndex, int count, bool vertical) => vertical ? count - visualIndex : visualIndex;
    internal static double Slot(WidgetConfig config) => DockVisualLayout.ItemSize(config) + (Vertical(config) && DockVisualLayout.ShowNames(config) ? 20 : 0);
    internal static double CrossSize(WidgetConfig config) => Vertical(config) ? DockVisualLayout.ItemSize(config) + 16 : DockVisualLayout.Height(config);
    internal static double Length(WidgetConfig config, int attention, int pinned, int running, int commands) =>
        DockAttentionLayout.Width(attention, pinned, commands, Slot(config), DockVisualLayout.ItemSpacing(config), running);
    internal static double Scale(WidgetConfig config, double available, int attention, int pinned, int running, int commands)
    {
        // In a vertical strip the four-DIP running-state row also consumes the main axis.
        int count = attention + pinned + running;
        double reserved = Vertical(config) ? 4 : 0;
        return DockAttentionLayout.ScaleToFit(available - count * reserved, attention, pinned, commands,
            Slot(config) - reserved, DockVisualLayout.ItemSpacing(config), running);
    }
}
