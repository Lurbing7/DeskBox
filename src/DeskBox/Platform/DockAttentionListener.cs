using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DeskBox.Platform;

internal sealed partial class DockAttentionListener : IDisposable
{
    private const nuint SubclassId = 0xDBD0;
    private static readonly Dictionary<nint, DockAttentionListener> Instances = [];
    private readonly nint _window;
    private readonly uint _message;
    private readonly uint _taskbarCreated;
    private readonly Dictionary<nint, string> _pending = [];
    private readonly HashSet<uint> _observedProcesses = [];
    public bool Available { get; private set; }
    public event Action? Changed;
    public event Action? WindowsChanged;
    public event Action<string>? Diagnostic;
    public IReadOnlyDictionary<nint, string> Pending => _pending;
    public unsafe DockAttentionListener(nint window)
    {
        _window = window;
        _message = RegisterWindowMessage("SHELLHOOK");
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        if (Instances.ContainsKey(window)) return;
        Instances[window] = this;
        bool attached = SetWindowSubclass(window, &Callback, SubclassId, 0);
        Available = attached && _message != 0 && RegisterShellHookWindow(window);
        if (!Available && attached) RemoveWindowSubclass(window, &Callback, SubclassId);
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint Callback(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (Instances.TryGetValue(window, out var listener))
            {
                if (message == listener._message) listener.OnShellEvent((uint)wParam, lParam);
                else if (listener._taskbarCreated != 0 && message == listener._taskbarCreated)
                {
                    listener.Available = RegisterShellHookWindow(window);
                    listener.Changed?.Invoke();
                    listener.WindowsChanged?.Invoke();
                }
            }
        }
        catch { }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    private void OnShellEvent(uint kind, nint window)
    {
        if (kind is 1 or 2 or 4 or 6 or 0x8004 or 0x8006) WindowsChanged?.Invoke();
        if (kind == 0x8006)
        {
            GetWindowThreadProcessId(window, out uint processId);
            if (processId == Environment.ProcessId) return;
            string? path = ExecutableForWindow(window);
            if (path is null) return;
            _pending[window] = path;
            _observedProcesses.Add(processId);
            Diagnostic?.Invoke($"flash {DescribeWindow(window)} foreground={DescribeWindow(GetForegroundWindow())}");
            Changed?.Invoke();
        }
        else if (kind is 4 or 0x8004)
        {
            GetWindowThreadProcessId(window, out uint processId);
            if (_observedProcesses.Contains(processId)) Diagnostic?.Invoke($"shell-activated {DescribeWindow(window)} foreground={DescribeWindow(GetForegroundWindow())}");
            string? path = ExecutableForWindow(window);
            if (path is not null) Clear(path);
        }
        else if (kind == 2 && _pending.Remove(window)) Changed?.Invoke();
    }
    public void Clear(string? path = null)
    {
        if (path is null) _pending.Clear();
        else foreach (nint handle in _pending.Where(p => string.Equals(p.Value, path, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToArray()) _pending.Remove(handle);
        Changed?.Invoke();
    }
    public bool HasAttention(string path) => _pending.Values.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
    public static unsafe string? ExecutableForWindow(nint window)
    {
        GetWindowThreadProcessId(window, out uint processId);
        nint process = OpenProcess(0x1000, false, processId);
        if (process == 0) return null;
        try { char* text = stackalloc char[2048]; uint size = 2048; return QueryFullProcessImageName(process, 0, text, ref size) ? new string(text, 0, (int)size) : null; }
        finally { CloseHandle(process); }
    }
    public static bool Activate(nint window)
    {
        if (!IsWindow(window)) return false;
        nint root = GetAncestor(window, 3);
        nint popup = GetLastActivePopup(root);
        if (popup != 0 && IsWindowVisible(popup)) window = popup;
        else if (root != 0 && IsWindowVisible(root)) window = root;
        if (!IsWindowVisible(window)) return false;
        if (IsIconic(window)) ShowWindow(window, 9);
        return SetForegroundWindow(window) && GetForegroundWindow() == window;
    }
    public static async Task<bool> ActivateNotifiedWindowAsync(nint window, Action<string>? diagnostic = null)
    {
        diagnostic?.Invoke($"click {DescribeWindow(window)} foreground={DescribeWindow(GetForegroundWindow())}");
        if (!IsWindow(window)) return false;
        GetWindowThreadProcessId(window, out uint processId);
        string executable = System.IO.Path.GetFileName(ExecutableForWindow(window) ?? "");
        bool isQq = executable.Equals("QQ.exe", StringComparison.OrdinalIgnoreCase);
        if (isQq || executable.Equals("Weixin.exe", StringComparison.OrdinalIgnoreCase) || executable.Equals("WeChat.exe", StringComparison.OrdinalIgnoreCase))
        {
            string tray = isQq ? "qq-tray" : "wechat-tray";
            diagnostic?.Invoke($"{tray} foreground-permission={AllowSetForegroundWindow(processId)}");
            var messages = new List<string>();
            bool invoked = await Task.Run(() => isQq
                ? DockQqTrayActivation.TryInvoke(processId, messages.Add)
                : DockWeChatTrayActivation.TryInvoke(processId, messages.Add));
            foreach (string message in messages) diagnostic?.Invoke(message);
            if (!invoked) return false;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                nint foreground = GetForegroundWindow();
                GetWindowThreadProcessId(foreground, out uint foregroundProcess);
                if (foregroundProcess == processId && IsWindowVisible(foreground))
                {
                    diagnostic?.Invoke($"{tray} foreground={DescribeWindow(foreground)}");
                    return true;
                }
                await Task.Delay(80);
            }
            return false;
        }
        if (!IsWindowVisible(window) || IsIconic(window))
        {
            // Let the application handle restoration, rather than exposing hidden UI
            // with ShowWindow or choosing an unrelated process main window.
            bool restored = await Task.Run(() => SendMessageTimeout(window, 0x112, 0xF120, 0, 0x2, 300, out _) != 0);
            diagnostic?.Invoke($"restore-command accepted={restored} {DescribeWindow(window)}");
            if (!restored) return false;
        }
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!IsWindow(window)) return false;
            GetWindowThreadProcessId(window, out uint currentProcessId);
            if (currentProcessId != processId) return false;
            if (IsWindowVisible(window) && !IsIconic(window))
            {
                nint target = window;
                nint popup = GetLastActivePopup(window);
                GetWindowThreadProcessId(popup, out uint popupProcessId);
                if (popup != 0 && popupProcessId == processId && IsWindowVisible(popup) && IsWindowEnabled(popup)) target = popup;
                bool activated = IsWindowEnabled(target) && SetForegroundWindow(target) && GetForegroundWindow() == target;
                diagnostic?.Invoke($"activate attempt={attempt} success={activated} target={DescribeWindow(target)} foreground={DescribeWindow(GetForegroundWindow())}");
                if (activated) return true;
            }
            await Task.Delay(80);
        }
        return false;
    }
    private static unsafe string DescribeWindow(nint window)
    {
        uint thread = GetWindowThreadProcessId(window, out uint process);
        char* name = stackalloc char[256];
        int length = GetClassName(window, name, 256);
        return $"hwnd=0x{window:X} pid={process} tid={thread} class={new string(name, 0, Math.Max(0, length))} visible={IsWindowVisible(window)} iconic={IsIconic(window)} enabled={IsWindowEnabled(window)} owner=0x{GetWindow(window, 4):X} rootOwner=0x{GetAncestor(window, 3):X} popup=0x{GetLastActivePopup(window):X}";
    }
    public unsafe void Dispose()
    {
        if (Available) { DeregisterShellHookWindow(_window); RemoveWindowSubclass(_window, &Callback, SubclassId); }
        if (Instances.TryGetValue(_window, out var owner) && ReferenceEquals(owner, this)) Instances.Remove(_window);
        _pending.Clear(); _observedProcesses.Clear(); Changed = null; WindowsChanged = null; Diagnostic = null;
    }
    [LibraryImport("user32.dll", EntryPoint="RegisterWindowMessageW", StringMarshalling=StringMarshalling.Utf16)] private static partial uint RegisterWindowMessage(string value);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static partial bool RegisterShellHookWindow(nint window);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static partial bool DeregisterShellHookWindow(nint window);
    [LibraryImport("comctl32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool SetWindowSubclass(nint w, delegate* unmanaged[Stdcall]<nint,uint,nuint,nint,nuint,nuint,nint> callback, nuint id, nuint data);
    [LibraryImport("comctl32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool RemoveWindowSubclass(nint w, delegate* unmanaged[Stdcall]<nint,uint,nuint,nint,nuint,nuint,nint> callback, nuint id);
    [LibraryImport("comctl32.dll")] private static partial nint DefSubclassProc(nint w,uint msg,nuint wp,nint lp);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint w,out uint pid);
    [LibraryImport("kernel32.dll")] private static partial nint OpenProcess(uint access,[MarshalAs(UnmanagedType.Bool)] bool inherit,uint pid);
    [LibraryImport("kernel32.dll",EntryPoint="QueryFullProcessImageNameW")][return:MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool QueryFullProcessImageName(nint p,uint flags,char* value,ref uint size);
    [LibraryImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint handle);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindow(nint w);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint w);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint w,int command);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint w);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static partial bool AllowSetForegroundWindow(uint processId);
    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint w, uint flags);
    [LibraryImport("user32.dll")] private static partial nint GetLastActivePopup(nint w);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(nint w);
    [LibraryImport("user32.dll", EntryPoint="SendMessageTimeoutW")] private static partial nint SendMessageTimeout(nint w, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [LibraryImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowEnabled(nint w);
    [LibraryImport("user32.dll", EntryPoint="GetClassNameW")] private static unsafe partial int GetClassName(nint w, char* name, int size);
    [LibraryImport("user32.dll")] private static partial nint GetWindow(nint w, uint command);
}
