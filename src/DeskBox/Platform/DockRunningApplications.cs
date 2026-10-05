using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DeskBox.Platform;

internal sealed record DockRunningWindow(nint Handle, uint ProcessId, string Title);
internal sealed record DockRunningApplication(string Key, string? Executable, string Name, IReadOnlyList<DockRunningWindow> Windows);

/// <summary>读取桌面应用窗口快照；不枚举后台进程，不修改系统任务栏。</summary>
internal static partial class DockRunningApplications
{
    private sealed class ScanState
    {
        internal readonly List<(string Key, string? Path, DockRunningWindow Window)> Windows = [];
        internal readonly Dictionary<uint, string?> Paths = [];
    }

    internal static unsafe IReadOnlyList<DockRunningApplication> Read()
    {
        var state = new ScanState();
        var handle = GCHandle.Alloc(state);
        try
        {
            if (!EnumWindows(&Collect, GCHandle.ToIntPtr(handle))) throw new InvalidOperationException("Window enumeration failed");
            return state.Windows.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new DockRunningApplication(group.Key, group.First().Path,
                    DisplayName(group.First().Path, group.First().Window.Title),
                    group.Select(item => item.Window).OrderBy(item => item.Handle).ToArray()))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { handle.Free(); }
    }

    internal static bool Include(uint process, bool visible, bool cloaked, long style, bool owned, string windowClass) =>
        process != 0 && process != Environment.ProcessId && visible && !cloaked &&
        (style & 0x80) == 0 && ((style & 0x40000) != 0 || (!owned && (style & 0x8000000) == 0)) &&
        windowClass is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "Windows.UI.Core.CoreWindow");

    internal static string GroupKey(string? path, uint process, nint window) =>
        path is null ? "pid:" + process : IsHost(path) ? "window:" + window : path;

    private static bool IsHost(string path) => Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
    private static string DisplayName(string? path, string title) =>
        path is null || IsHost(path) ? title : Path.GetFileNameWithoutExtension(path);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int Collect(nint window, nint parameter)
    {
        // Native callbacks must not propagate managed exceptions across the ABI.
        try
        {
            var state = (ScanState)GCHandle.FromIntPtr(parameter).Target!;
            GetWindowThreadProcessId(window, out uint process);
            char* classText = stackalloc char[256];
            int classLength = GetClassName(window, classText, 256);
            DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int));
            if (!Include(process, IsWindowVisible(window), cloaked != 0, (long)GetWindowLongPtr(window, -20), GetWindow(window, 4) != 0,
                new string(classText, 0, Math.Max(0, classLength)))) return 1;
            char* titleText = stackalloc char[1024];
            int length = GetWindowText(window, titleText, 1024);
            if (length <= 0) return 1;
            string title = new(titleText, 0, length);
            if (!state.Paths.TryGetValue(process, out string? path))
                state.Paths[process] = path = DockAttentionListener.ExecutableForWindow(window);
            state.Windows.Add((GroupKey(path, process, window), path, new(window, process, title)));
        }
        catch { }
        return 1;
    }

    internal static bool IsCurrent(DockRunningWindow window) =>
        GetWindowThreadProcessId(window.Handle, out uint process) != 0 && process == window.ProcessId;

    // Ask the application to close normally; its save/cancel workflow remains in control.
    internal static bool RequestClose(DockRunningWindow window) => IsCurrent(window) && PostMessage(window.Handle, 0x10, 0, 0);

    internal static bool CanPin(DockRunningApplication app) => app.Executable is { } path &&
        !IsHost(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    /// <summary>复制窗口图标后读取，避免释放应用所有的 HICON；消息超时限制在 100ms。</summary>
    internal static byte[]? ReadIcon(DockRunningWindow window)
    {
        if (!IsCurrent(window)) return null;
        SendMessageTimeout(window.Handle, 0x7F, 1, 0, 0x2, 100, out nuint result);
        nint source = (nint)result;
        if (source == 0) source = GetClassLongPtr(window.Handle, -14);
        if (source == 0)
        {
            SendMessageTimeout(window.Handle, 0x7F, 0, 0, 0x2, 100, out result);
            source = (nint)result;
        }
        nint copied = source == 0 ? 0 : CopyIcon(source);
        if (copied == 0) return null;
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(copied);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }
        catch { return null; }
        finally { DestroyIcon(copied); }
    }

    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint parameter);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(nint window);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint window, out uint process);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")] private static unsafe partial int GetWindowText(nint window, char* text, int capacity);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] private static unsafe partial int GetClassName(nint window, char* text, int capacity);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static partial nint GetWindowLongPtr(nint window, int index);
    [LibraryImport("user32.dll")] private static partial nint GetWindow(nint window, uint command);
    [LibraryImport("dwmapi.dll")] private static partial int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);
    [LibraryImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static partial nint GetClassLongPtr(nint window, int index);
    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static partial nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [LibraryImport("user32.dll")] private static partial nint CopyIcon(nint icon);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyIcon(nint icon);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
