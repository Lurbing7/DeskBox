using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DeskBox.Platform;

// Activate the application's registered tray entry so its own show/update logic runs.
// Native HWND restoration alone does not restore Qt's internal hidden-widget state.
internal static unsafe partial class DockWeChatTrayActivation
{
    private static readonly Guid AutomationClass = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    private static readonly Guid AutomationInterface = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
    private static readonly Guid InvokeInterface = new("fb377fbe-8ea6-46d5-9c73-6499642d3059");

    public static bool TryInvoke(uint processId, Action<string>? diagnostic, bool invoke = true)
    {
        nint previousDpi = SetThreadDpiAwarenessContext(-4);
        int initialized = CoInitializeEx(0, 0);
        if (initialized < 0) { if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi); return false; }
        nint automation = 0, root = 0, condition = 0, elements = 0, selected = 0, pattern = 0;
        try
        {
            var icons = new List<Rect>();
            var state = GCHandle.Alloc((processId, icons));
            try { EnumWindows(&CollectTrayIcon, GCHandle.ToIntPtr(state)); }
            finally { state.Free(); }
            if (icons.Count != 1) { diagnostic?.Invoke($"wechat-tray registered-icons={icons.Count}"); return false; }
            nint shell = FindWindow("Shell_TrayWnd", null);
            if (shell == 0 || CoCreateInstance(in AutomationClass, 0, 1, in AutomationInterface, out automation) < 0) return false;
            if (((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)VTable(automation)[6])(automation, shell, &root) < 0) return false;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)VTable(automation)[21])(automation, &condition) < 0) return false;
            if (((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)VTable(root)[6])(root, 4, condition, &elements) < 0) return false;
            int count = 0, matches = 0;
            if (((delegate* unmanaged[Stdcall]<nint, int*, int>)VTable(elements)[3])(elements, &count) < 0) return false;
            Rect icon = icons[0];
            for (int index = 0; index < Math.Min(count, 512); index++)
            {
                nint element = 0;
                try
                {
                    if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)VTable(elements)[4])(elements, index, &element) < 0) continue;
                    Rect bounds = default;
                    string id = ReadString(element, 29), className = ReadString(element, 30);
                    if (!invoke && id == "NotifyItemIcon")
                    {
                        ((delegate* unmanaged[Stdcall]<nint, Rect*, int>)VTable(element)[43])(element, &bounds);
                        diagnostic?.Invoke($"tray-read class={className} bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom} icon={icon.Left},{icon.Top},{icon.Right},{icon.Bottom}");
                    }
                    if (id != "NotifyItemIcon" || className != "SystemTray.NormalButton" ||
                        ((delegate* unmanaged[Stdcall]<nint, Rect*, int>)VTable(element)[43])(element, &bounds) < 0 || !ContainsIcon(bounds, icon)) continue;
                    matches++;
                    if (selected == 0) { selected = element; element = 0; }
                }
                finally { Release(element); }
            }
            diagnostic?.Invoke($"wechat-tray matching-buttons={matches}");
            if (matches != 1 || selected == 0) return false;
            Guid invokeId = InvokeInterface;
            if (((delegate* unmanaged[Stdcall]<nint, int, Guid*, nint*, int>)VTable(selected)[14])(selected, 10000, &invokeId, &pattern) < 0) return false;
            if (!invoke) { diagnostic?.Invoke("wechat-tray invoke-pattern-available=True (read-only)"); return true; }
            bool accepted = ((delegate* unmanaged[Stdcall]<nint, int>)VTable(pattern)[3])(pattern) >= 0;
            diagnostic?.Invoke($"wechat-tray invoke-accepted={accepted}");
            return accepted;
        }
        catch (Exception error) { diagnostic?.Invoke($"wechat-tray failure={error.GetType().Name}"); return false; }
        finally
        {
            Release(pattern); Release(selected); Release(elements); Release(condition); Release(root); Release(automation);
            CoUninitialize();
            if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    internal static bool ContainsIcon(Rect button, Rect icon) => icon.Right > icon.Left && icon.Bottom > icon.Top &&
        button.Right > button.Left && button.Bottom > button.Top &&
        button.Left <= icon.Left && button.Top <= icon.Top && button.Right >= icon.Right && button.Bottom >= icon.Bottom;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CollectTrayIcon(nint window, nint data)
    {
        try
        {
            var state = ((uint ProcessId, List<Rect> Icons))GCHandle.FromIntPtr(data).Target!;
            GetWindowThreadProcessId(window, out uint processId);
            if (processId != state.ProcessId) return 1;
            char* text = stackalloc char[256];
            int length = GetClassName(window, text, 256);
            if (new string(text, 0, Math.Max(0, length)) != "Qt51514WxTrayIconMessageWindowClass") return 1;
            var identity = new IconIdentifier { Size = (uint)sizeof(IconIdentifier), Window = window };
            if (Shell_NotifyIconGetRect(ref identity, out Rect icon) == 0 && icon.Right > icon.Left && icon.Bottom > icon.Top) state.Icons.Add(icon);
        }
        catch { }
        return 1;
    }
    private static string ReadString(nint element, int slot)
    {
        nint value = 0;
        try { return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)VTable(element)[slot])(element, &value) >= 0 && value != 0 ? Marshal.PtrToStringBSTR(value) : ""; }
        finally { if (value != 0) Marshal.FreeBSTR(value); }
    }
    private static nint* VTable(nint value) => *(nint**)value;
    private static void Release(nint value) { if (value != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)VTable(value)[2])(value); }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct IconIdentifier { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [LibraryImport("user32.dll")] private static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint data);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
    [LibraryImport("user32.dll")] private static partial nint SetThreadDpiAwarenessContext(nint context);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] private static partial int GetClassName(nint window, char* name, int size);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)] private static partial nint FindWindow(string className, string? title);
    [LibraryImport("shell32.dll")] private static partial int Shell_NotifyIconGetRect(ref IconIdentifier identity, out Rect rect);
    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint mode);
    [LibraryImport("ole32.dll")] private static partial void CoUninitialize();
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint value);
}
