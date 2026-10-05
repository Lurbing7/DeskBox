using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class DockWidgetContent
{
    private string _optionsSignature = "";
    private readonly DispatcherTimer _pointerHoverTimer = new();
    private (DockEntry Entry, Button Button, int Depth)? _pointerHover;
    private int _pointerHoverVersion;

    private string OptionsSignature() => string.Join("|", new[]
    {
        "DockIconSize", "DockShowNames", "DockShowMoreButton", "DockAutoSize", "DockPosition",
        "DockMaximumRatio", "DockTransparent", "DockSpacing", "DockEdgeMargin", "DockLockItems",
        "DockExpandDelay", "DockHoverExpand", "DockWheelItems", "DockShowRunning", "DockOrder"
    }.Select(key => Config.Metadata.GetValueOrDefault(key) ?? ""));

    internal void ApplyOptionsIfChanged()
    {
        string signature = OptionsSignature();
        if (_disposed || signature == _optionsSignature) return;
        _optionsSignature = signature;
        CancelPointerHover();
        ApplyDockOrientation();
        ConfigureRunningTracking();
        _ = RefreshAsync();
    }

    private void AttachPointerHover(Button button, DockEntry entry, int depth)
    {
        button.PointerEntered += (_, e) =>
        {
            if (!DockVisualLayout.HoverExpand(Config) || _dragHoverActive || _itemMenuOpen || SuppressDockClick ||
                e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
            CancelPointerHover();
            _pointerHover = (entry, button, depth);
            _pointerHoverTimer.Interval = TimeSpan.FromMilliseconds(DockVisualLayout.ExpandDelay(Config));
            _pointerHoverTimer.Tick -= PointerHoverTick;
            _pointerHoverTimer.Tick += PointerHoverTick;
            _pointerHoverTimer.Start();
        };
        button.PointerExited += (_, _) => { if (_pointerHover?.Button == button) CancelPointerHover(); };
        button.Unloaded += (_, _) => { if (_pointerHover?.Button == button) CancelPointerHover(); };
    }

    private async void PointerHoverTick(object? sender, object args)
    {
        _pointerHoverTimer.Stop();
        int version = _pointerHoverVersion;
        if (_disposed || _pointerHover is not { } hover || _dragHoverActive || _itemMenuOpen || !DockVisualLayout.HoverExpand(Config)) return;
        try
        {
            await OpenFolder(hover.Entry, hover.Button, hover.Depth, dragHover: true,
                stillValid: () => version == _pointerHoverVersion && !_dragHoverActive && !_itemMenuOpen && DockVisualLayout.HoverExpand(Config));
        }
        catch { if (!_disposed) SetStatus(T("Dock.ReadFailed")); }
    }

    private void CancelPointerHover()
    {
        _pointerHoverTimer.Stop();
        _pointerHover = null;
        _pointerHoverVersion++;
    }
}
