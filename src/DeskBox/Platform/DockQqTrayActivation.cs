using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DeskBox.Platform;

internal static unsafe partial class DockQqTrayActivation
{
    public static bool TryInvoke(uint processId, Action<string>? diagnostic, bool invoke = true)
    {
        if (processId == 0) return false;
        nint previousDpi = SetThreadDpiAwarenessContext(-4);
        try
        {
            var icons = new List<RegisteredIcon>();
            var state = GCHandle.Alloc((processId, icons));
            try { EnumWindows(&CollectTrayIcons, GCHandle.ToIntPtr(state)); }
            finally { state.Free(); }
            diagnostic?.Invoke($"qq-tray registered-icons={icons.Count}");
            // Multiple registered icons cannot be selected safely by position/name.
            if (icons.Count != 1) return false;
            var selected = icons[0];
            if (!TryReadIcon(selected.Window, processId, selected.Id)) return false;
            if (!invoke) { diagnostic?.Invoke($"qq-tray id={selected.Id} callback-available=True (read-only)"); return true; }
            // Electron's legacy protocol carries the icon ID in wParam and the
            // click event in lParam. This is not the Qt version-4 packing.
            bool accepted = SendMessageTimeout(selected.Window, 0x8001, selected.Id, 0x201, 0x2, 300, out _) != 0;
            diagnostic?.Invoke($"qq-tray callback-message=0x8001 id={selected.Id} event=WM_LBUTTONDOWN delivery-accepted={accepted}");
            return accepted;
        }
        catch (Exception error) { diagnostic?.Invoke($"qq-tray failure={error.GetType().Name}"); return false; }
        finally { if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi); }
    }

    private readonly record struct RegisteredIcon(nint Window, uint Id);
    internal static bool IsSupportedTrayWindow(uint expectedProcess, uint actualProcess, string windowClass) =>
        expectedProcess != 0 && expectedProcess == actualProcess && windowClass == "Electron_NotifyIconHostWindow";

    private static bool IsSupportedWindow(nint window, uint processId)
    {
        GetWindowThreadProcessId(window, out uint owner);
        if (owner != processId) return false;
        char* name = stackalloc char[256];
        int length = GetClassName(window, name, 256);
        return IsSupportedTrayWindow(processId, owner, new string(name, 0, Math.Max(0, length)));
    }
    private static bool TryReadIcon(nint window, uint processId, uint id)
    {
        if (!IsSupportedWindow(window, processId)) return false;
        var identity = new IconIdentifier { Size = (uint)sizeof(IconIdentifier), Window = window, Id = id };
        return Shell_NotifyIconGetRect(ref identity, out Rect rect) == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CollectTrayIcons(nint window, nint data)
    {
        try
        {
            var state = ((uint ProcessId, List<RegisteredIcon> Icons))GCHandle.FromIntPtr(data).Target!;
            if (!IsSupportedWindow(window, state.ProcessId)) return 1;
            // Electron starts at ID 3; keep this targeted lookup bounded and
            // reject ambiguity rather than assuming every QQ session uses ID 3.
            for (uint id = 3; id <= 255; id++)
                if (TryReadIcon(window, state.ProcessId, id)) state.Icons.Add(new(window, id));
        }
        catch { }
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct IconIdentifier { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [LibraryImport("user32.dll")] private static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint data);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
    [LibraryImport("user32.dll")] private static partial nint SetThreadDpiAwarenessContext(nint context);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] private static partial int GetClassName(nint window, char* name, int size);
    [LibraryImport("shell32.dll")] private static partial int Shell_NotifyIconGetRect(ref IconIdentifier identity, out Rect rect);
    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static partial nint SendMessageTimeout(nint window, uint message, nuint wp, nint lp, uint flags, uint timeout, out nuint result);
}
