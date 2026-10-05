using System.Runtime.InteropServices;
using DeskBox.Models;

namespace DeskBox.Platform;

// NVML is supplied by the installed NVIDIA driver, never copied from another application.
internal sealed unsafe partial class SystemMonitorNvml : IDisposable
{
    private nint _library;
    private bool _initialized;
    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapter { public uint Low; public int High; public uint Handle; }
    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapter { public uint Handle; public int Type; public nint Data; public uint Size; }
    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterAddress { public uint Bus, Device, Function; }
    [LibraryImport("gdi32.dll", EntryPoint = "D3DKMTOpenAdapterFromLuid")]
    private static partial int Open(ref OpenAdapter adapter);
    [LibraryImport("gdi32.dll", EntryPoint = "D3DKMTQueryAdapterInfo")]
    private static partial int Query(ref QueryAdapter adapter);
    [LibraryImport("gdi32.dll", EntryPoint = "D3DKMTCloseAdapter")]
    private static partial int Close(ref uint handle);
    [StructLayout(LayoutKind.Sequential)]
    private struct PciInfo
    {
        public fixed byte LegacyBusId[16];
        public uint Domain, Bus, Device, DeviceId, SubsystemId;
        public fixed byte BusId[32];
    }
    public SystemMonitorNvml()
    {
        try
        {
            if (!NativeLibrary.TryLoad(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll"), out _library)) return;
            var init = (delegate* unmanaged[Cdecl]<int>)Export("nvmlInit_v2");
            _initialized = init() == 0;
        }
        catch { Dispose(); }
    }
    private nint Export(string name) => NativeLibrary.GetExport(_library, name);
    public MonitorSensors Read(MonitorDevice? selected, IReadOnlyList<MonitorDevice> adapters)
    {
        if (!_initialized || selected is not { VendorId: 0x10de }) return new();
        try
        {
            var parts = selected.Id.Split('_');
            var adapter = new OpenAdapter { High = unchecked((int)Convert.ToUInt32(parts[0], 16)), Low = Convert.ToUInt32(parts[1], 16) };
            if (Open(ref adapter) < 0) return new();
            AdapterAddress location = default;
            try
            {
                var query = new QueryAdapter { Handle = adapter.Handle, Type = 6, Data = (nint)(&location), Size = (uint)sizeof(AdapterAddress) };
                if (Query(ref query) < 0) return new();
            }
            finally { Close(ref adapter.Handle); }
            if (location.Function != 0) return new();
            var countFn = (delegate* unmanaged[Cdecl]<uint*, int>)Export("nvmlDeviceGetCount_v2");
            var handleFn = (delegate* unmanaged[Cdecl]<uint, nint*, int>)Export("nvmlDeviceGetHandleByIndex_v2");
            var pciFn = (delegate* unmanaged[Cdecl]<nint, PciInfo*, int>)Export("nvmlDeviceGetPciInfo_v3");
            uint count = 0;
            if (countFn(&count) != 0 || count > 64) return new();
            nint matching = 0;
            for (uint index = 0; index < count; index++)
            {
                nint device = 0; PciInfo pci = default;
                if (handleFn(index, &device) == 0 && pciFn(device, &pci) == 0 &&
                    (pci.DeviceId & 0xffff) == selected.VendorId && (pci.DeviceId >> 16) == selected.DeviceId &&
                    pci.Domain == 0 && pci.Bus == location.Bus && pci.Device == location.Device)
                {
                    if (matching != 0) return new(); // Ambiguous identical GPUs must not receive another GPU's values.
                    matching = device;
                }
            }
            if (matching == 0) return new();
            return new(GpuTemperature: ReadTemperature(matching), GpuPower: ReadPower(matching), GpuMhz: ReadClock(matching));
        }
        catch { return new(); }
    }
    private double? ReadTemperature(nint device)
    {
        if (!NativeLibrary.TryGetExport(_library, "nvmlDeviceGetTemperature", out nint address)) return null;
        uint value = 0;
        return ((delegate* unmanaged[Cdecl]<nint, uint, uint*, int>)address)(device, 0, &value) == 0 && value is > 0 and < 150 ? value : null;
    }
    private double? ReadPower(nint device)
    {
        if (!NativeLibrary.TryGetExport(_library, "nvmlDeviceGetPowerUsage", out nint address)) return null;
        uint value = 0;
        return ((delegate* unmanaged[Cdecl]<nint, uint*, int>)address)(device, &value) == 0 ? value / 1000d : null;
    }
    private double? ReadClock(nint device)
    {
        if (!NativeLibrary.TryGetExport(_library, "nvmlDeviceGetClockInfo", out nint address)) return null;
        uint value = 0;
        return ((delegate* unmanaged[Cdecl]<nint, uint, uint*, int>)address)(device, 0, &value) == 0 && value > 0 ? value : null;
    }
    public void Dispose()
    {
        if (_library == 0) return;
        if (_initialized && NativeLibrary.TryGetExport(_library, "nvmlShutdown", out nint address))
            ((delegate* unmanaged[Cdecl]<int>)address)();
        _initialized = false;
        NativeLibrary.Free(_library); _library = 0;
    }
}
