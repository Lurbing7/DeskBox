using System.Runtime.InteropServices;

namespace DeskBox.Platform;

internal sealed partial class SystemMonitorPdhQuery : IDisposable
{
    private nint _query;
    private readonly Dictionary<string, nint> _counters = [];
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValue { public uint Status; public double Value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem { public nint Name; public CounterValue Value; }
    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint Open(string? source, nuint data, out nint query);
    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint Add(nint query, string path, nuint data, out nint counter);
    [LibraryImport("pdh.dll", EntryPoint = "PdhCollectQueryData")]
    private static partial uint Collect(nint query);
    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterValue")]
    private static partial uint Value(nint counter, uint format, out uint type, out CounterValue value);
    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static partial uint Array(nint counter, uint format, ref uint size, out uint count, nint buffer);
    [LibraryImport("pdh.dll", EntryPoint = "PdhCloseQuery")]
    private static partial uint Close(nint query);

    public SystemMonitorPdhQuery()
    {
        if (Open(null, 0, out _query) != 0) return;
        AddCounter("cpu", @"\Processor(_Total)\% Processor Time");
        AddCounter("frequency", @"\Processor Information(_Total)\Processor Frequency");
        AddCounter("gpu", @"\GPU Engine(*)\Utilization Percentage");
        AddCounter("vram", @"\GPU Adapter Memory(*)\Dedicated Usage");
    }
    private void AddCounter(string name, string path)
    {
        if (Add(_query, path, 0, out nint counter) == 0) _counters[name] = counter;
    }
    public bool Sample() => _query != 0 && Collect(_query) == 0;
    public double? Read(string name)
    {
        return _counters.TryGetValue(name, out nint counter) && Value(counter, 0x200 | 0x8000, out _, out var value) == 0 &&
            value.Status <= 1 && double.IsFinite(value.Value) && value.Value >= 0 ? value.Value : null;
    }
    public List<(string Name, double Value)> ReadArray(string name)
    {
        var result = new List<(string, double)>();
        if (!_counters.TryGetValue(name, out nint counter)) return result;
        uint size = 0;
        Array(counter, 0x200 | 0x8000, ref size, out _, 0);
        if (size == 0 || size > 16 * 1024 * 1024) return result;
        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (Array(counter, 0x200 | 0x8000, ref size, out uint count, buffer) != 0) return result;
            int itemSize = Marshal.SizeOf<CounterItem>();
            if ((ulong)count * (uint)itemSize > size) return result;
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(buffer + i * itemSize);
                if (item.Value.Status <= 1 && double.IsFinite(item.Value.Value) && item.Value.Value >= 0 && Marshal.PtrToStringUni(item.Name) is { } label)
                    result.Add((label, item.Value.Value));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }
    public void Dispose() { if (_query != 0) { Close(_query); _query = 0; } }
}
