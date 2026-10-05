using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class DockWidgetContent
{
    private void AttachChildWheelScrolling(ScrollViewer scroll)
    {
        double? wheelTarget = null;
        scroll.ViewChanged += (_, e) => { if (!e.IsIntermediate) wheelTarget = null; };
        scroll.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, e) =>
        {
            var properties = e.GetCurrentPoint(scroll).Properties;
            // Native horizontal wheel and precision-touchpad panning keep their
            // own direction/inertia; translating them again would double-scroll.
            if (properties.IsHorizontalMouseWheel) { wheelTarget = null; return; }
            bool nativeShiftWheel = e.Handled && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (nativeShiftWheel) { wheelTarget = null; return; }
            if (properties.MouseWheelDelta == 0 || scroll.ScrollableWidth <= 0) return;
            double step = (DockVisualLayout.ItemSize(Config) + DockVisualLayout.ItemSpacing(Config)) * DockVisualLayout.WheelItems(Config);
            double target = Math.Clamp((wheelTarget ?? scroll.HorizontalOffset) - properties.MouseWheelDelta / 120.0 * step, 0, scroll.ScrollableWidth);
            wheelTarget = target;
            if (!scroll.ChangeView(target, null, null)) wheelTarget = null;
            e.Handled = true;
        }), handledEventsToo: true);
    }
}
