namespace DeskBox.Models;

internal sealed record MonitorDevice(string Id, string Name, ulong DedicatedMemory = 0, uint VendorId = 0, uint DeviceId = 0);
internal sealed record MonitorSensors(double? CpuTemperature = null, double? CpuPower = null,
    double? GpuTemperature = null, double? GpuPower = null, double? GpuMhz = null);
internal sealed record SystemMonitorOptions(int IntervalSeconds, string GpuId, string NetworkId, bool ShowUnavailable,
    int Metrics = DeskBox.Services.SystemMonitorSelection.All)
{
    public static SystemMonitorOptions Read(WidgetConfig config)
    {
        int.TryParse(config.Metadata.GetValueOrDefault("MonitorInterval"), out int interval);
        return new(interval is 1 or 2 or 5 ? interval : 1,
            config.Metadata.GetValueOrDefault("MonitorGpu") ?? "",
            config.Metadata.GetValueOrDefault("MonitorNetwork") ?? "",
            config.Metadata.GetValueOrDefault("MonitorUnavailable") != "false",
            DeskBox.Services.SystemMonitorSelection.Read(config.Metadata));
    }
}
internal sealed record SystemMonitorSnapshot(
    DateTimeOffset Timestamp, double? CpuLoad, double? CpuMhz,
    ulong? MemoryUsed, ulong? MemoryTotal,
    IReadOnlyList<MonitorDevice> Gpus, string? GpuId, double? GpuLoad,
    ulong? VramUsed, ulong? VramTotal,
    IReadOnlyList<MonitorDevice> Networks, string? NetworkId,
    double? UploadBytes, double? DownloadBytes, bool Warmup, bool DeviceFallback,
    string? NetworkAddress = null, MonitorSensors? Sensors = null);
