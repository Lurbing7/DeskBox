namespace DeskBox.Services;

internal static class SystemMonitorSelection
{
    internal static readonly string[] Keys = ["CpuLoad", "CpuFrequency", "CpuTemperature", "CpuFan", "CpuPower",
        "GpuLoad", "GpuFrequency", "GpuTemperature", "GpuFan", "GpuPower", "Memory", "Vram", "Upload", "Download", "IP"];
    internal const int All = (1 << 15) - 1;
    internal static int Read(IReadOnlyDictionary<string, string> metadata)
    {
        int mask = All;
        for (int i = 0; i < Keys.Length; i++)
            if (metadata.TryGetValue("MonitorMetric." + Keys[i], out string? value) && value == "false") mask &= ~(1 << i);
        return mask;
    }
    internal static bool Has(int mask, string key) => Array.IndexOf(Keys, key) is int index && index >= 0 && (mask & (1 << index)) != 0;
    internal static bool Any(int mask, params string[] keys) => keys.Any(key => Has(mask, key));
    internal static bool CpuSensors(int mask) => Any(mask, "CpuTemperature", "CpuPower");
    internal static bool GpuSensors(int mask) => Any(mask, "GpuTemperature", "GpuPower", "GpuFrequency");
    internal static bool GpuDevices(int mask) => GpuSensors(mask) || Any(mask, "GpuLoad", "Vram");
    internal static bool Network(int mask) => Any(mask, "Upload", "Download", "IP");
    internal static bool NetworkRates(int mask) => Any(mask, "Upload", "Download");
}
