namespace DeskBox.Services;

internal static class SystemMonitorMetrics
{
    public static double? Rate(long previous, long current, double seconds) =>
        previous >= 0 && current >= previous && double.IsFinite(seconds) && seconds > 0
            ? (current - previous) / seconds : null;
    public static double? Percentage(double value) => double.IsFinite(value) && value >= 0 ? Math.Min(100, value) : null;
    public static Dictionary<string, double> GpuLoad(IEnumerable<(string Adapter, string Engine, double Value)> samples) =>
        samples.Where(s => double.IsFinite(s.Value) && s.Value >= 0)
            .GroupBy(s => (s.Adapter, s.Engine))
            .Select(g => (g.Key.Adapter, Load: Math.Min(100, g.Sum(s => s.Value))))
            .GroupBy(s => s.Adapter).ToDictionary(g => g.Key, g => g.Max(s => s.Load));

    public static string? AdapterKey(string name)
    {
        int start = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        string[] parts = name[(start + 5)..].Split('_');
        return parts.Length >= 2 ? (parts[0] + "_" + parts[1]).ToLowerInvariant() : null;
    }
    public static string EngineKey(string name)
    {
        int start = name.IndexOf("_phys_", StringComparison.OrdinalIgnoreCase);
        return start >= 0 ? name[start..] : name;
    }
}
