using System.Runtime.InteropServices;
using DeskBox.Platform;

namespace DeskBox.Tests;

// A local tray owner exercises production delivery without touching QQ.
internal static class QqTrayCallbackFixture
{
    private const string WindowClass = "Electron_NotifyIconHostWindow";
    private static int _activated;
    private static uint _iconId;
    private static readonly WindowProcedure Procedure = HandleMessage;
    public static (bool Delivered, int Activated) Run(uint iconId = 3, bool secondIcon = false, bool registerIcon = true, bool invoke = true)
    {
        _activated = 0;
        _iconId = iconId;
        nint instance = GetModuleHandle(null);
        var cls = new WindowClassData { Size = (uint)Marshal.SizeOf<WindowClassData>(), Instance = instance,
            Procedure = Marshal.GetFunctionPointerForDelegate(Procedure), Name = WindowClass };
        if (RegisterClassEx(ref cls) == 0) throw new InvalidOperationException("Fixture class registration failed.");
        nint window = CreateWindowEx(0, WindowClass, "DeskBox tray callback test", 0, 0, 0, 0, 0, 0, 0, instance, 0);
        var icon = new NotificationIcon { Size = (uint)Marshal.SizeOf<NotificationIcon>(), Window = window, Id = iconId, Flags = 3,
            Callback = 0x8001, Icon = LoadIcon(0, 32512), Tip = "DeskBox callback test", Info = "", Title = "", Version = 0 };
        var second = icon;
        second.Id++;
        bool registered = false, secondRegistered = false;
        try
        {
            if (registerIcon)
            {
                registered = Shell_NotifyIcon(0, ref icon);
                if (!registered) throw new InvalidOperationException("Fixture tray registration failed.");
                if (secondIcon)
                {
                    secondRegistered = Shell_NotifyIcon(0, ref second);
                    if (!secondRegistered) throw new InvalidOperationException("Second fixture tray registration failed.");
                }
            }
            Task<bool> work = Task.Run(() => DockQqTrayActivation.TryInvoke((uint)Environment.ProcessId, Console.WriteLine, invoke));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!work.IsCompleted && DateTime.UtcNow < deadline)
            {
                while (PeekMessage(out var message, 0, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); }
                Thread.Sleep(5);
            }
            bool delivered = work.IsCompleted && work.GetAwaiter().GetResult();
            Console.WriteLine($"Fixture delivery={delivered}, application-activation={_activated}");
            return (delivered, _activated);
        }
        finally
        {
            if (secondRegistered) Shell_NotifyIcon(2, ref second);
            if (registered) Shell_NotifyIcon(2, ref icon);
            if (window != 0) DestroyWindow(window);
            UnregisterClass(WindowClass, instance);
        }
    }
    private static nint HandleMessage(nint window, uint message, nuint wp, nint lp)
    {
        if (message == 0x8001 && wp == _iconId && lp == 0x201) Interlocked.Increment(ref _activated);
        return DefWindowProc(window, message, wp, lp);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WindowProcedure(nint w, uint message, nuint wp, nint lp);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClassData
    {
        public uint Size, Style; public nint Procedure; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background; public string? Menu; public string Name; public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotificationIcon
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WindowClassData data);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nuint wp, nint lp);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint command, ref NotificationIcon data);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
}
