using System.Runtime.InteropServices;
using DeskBox.Models;

namespace DeskBox.Platform;

internal static partial class SystemMonitorNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatus
    {
        public uint Length, Load;
        public ulong Total, Available, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended;
    }
    [LibraryImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMemory(ref MemoryStatus status);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct AdapterDescription
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }
    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(in Guid iid, out nint factory);

    internal static unsafe List<MonitorDevice> GetGpus()
    {
        var result = new List<MonitorDevice>();
        Guid iid = new("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(in iid, out nint factory) < 0 || factory == 0) return result;
        try
        {
            var factoryTable = *(nint**)factory;
            for (uint index = 0; index < 32; index++)
            {
                nint adapter = 0;
                int hr = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)factoryTable[12])(factory, index, &adapter);
                if (hr < 0 || adapter == 0) break;
                try
                {
                    AdapterDescription description = default;
                    var table = *(nint**)adapter;
                    if (((delegate* unmanaged[Stdcall]<nint, AdapterDescription*, int>)table[10])(adapter, &description) >= 0 && (description.Flags & 2) == 0)
                    {
                        string id = $"0x{unchecked((uint)description.LuidHigh):x8}_0x{description.LuidLow:x8}";
                        result.Add(new(id, new string(description.Description), (ulong)description.DedicatedVideoMemory, description.VendorId, description.DeviceId));
                    }
                }
                finally { Release(adapter); }
            }
        }
        finally { Release(factory); }
        return result;
    }
    private static unsafe void Release(nint instance) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2])(instance);
}
