using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace DeskBox.Platform;

internal sealed class DockOutsideClickObserver : IDisposable
{
    private readonly Win32Helper.LowLevelMouseProc _callback;
    private nint _hook;
    private bool _disposed;

    public DockOutsideClickObserver(DispatcherQueue dispatcher, Action<int, int> clicked)
    {
        _callback = (code, message, data) =>
        {
            if (code >= 0 && message == (nint)Win32Helper.WM_LBUTTONDOWN)
            {
                var point = Marshal.PtrToStructure<Win32Helper.MSLLHOOKSTRUCT>(data).pt;
                dispatcher.TryEnqueue(() => { if (!_disposed) clicked(point.X, point.Y); });
            }
            return Win32Helper.CallNextHookEx(_hook, code, message, data);
        };
        _hook = Win32Helper.SetWindowsMouseHookEx(Win32Helper.WH_MOUSE_LL, _callback, Win32Helper.GetModuleHandle(null), 0);
        if (_hook == 0) App.Log($"[Dock] Outside click observer unavailable error={Marshal.GetLastWin32Error()}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != 0) { Win32Helper.UnhookWindowsHookEx(_hook); _hook = 0; }
    }
}
