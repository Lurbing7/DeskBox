using System.Diagnostics;
using System.Net.NetworkInformation;
using DeskBox.Models;
using DeskBox.Platform;

namespace DeskBox.Services;

internal sealed class SystemMonitorSampler : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task _loop = Task.CompletedTask;
    private bool _disposed;
    private SystemMonitorOptions _options;
    public SystemMonitorOptions Options { get => Volatile.Read(ref _options); set => Volatile.Write(ref _options, value); }
    public event Action<SystemMonitorSnapshot>? Updated;
    public event Action<Exception>? Failed;
    public SystemMonitorSampler(SystemMonitorOptions options) => _options = options;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _cancellation is not null) return;
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            Task previous = _loop;
            _loop = Task.Run(async () =>
            {
                try { await previous.ConfigureAwait(false); if (!cancellation.IsCancellationRequested) await Run(cancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception ex) { Failed?.Invoke(ex); }
                finally
                {
                    lock (_gate) { if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null; }
                    cancellation.Dispose();
                }
            });
        }
    }
    public void Stop()
    {
        lock (_gate) { _cancellation?.Cancel(); _cancellation = null; }
    }
    internal Task Completion { get { lock (_gate) return _loop; } }
    private async Task Run(CancellationToken token)
    {
        int metrics = Options.Metrics;
        if (metrics == 0) return;
        bool Has(string key) => SystemMonitorSelection.Has(metrics, key);
        using var pdh = new SystemMonitorPdhQuery(metrics);
        using var nvml = SystemMonitorSelection.GpuSensors(metrics) ? new SystemMonitorNvml() : null;
        using var cpuBridge = SystemMonitorSelection.CpuSensors(metrics) ? new SystemMonitorCpuBridge() : null;
        List<MonitorDevice> gpus;
        try { gpus = SystemMonitorSelection.GpuDevices(metrics) ? SystemMonitorNative.GetGpus() : []; } catch { gpus = []; }
        string? previousNetwork = null;
        long previousSent = 0, previousReceived = 0;
        long previousTime = 0;
        bool warmup = true;
        int iteration = 1;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            SystemMonitorOptions options = Options;
            try
            {
                if (SystemMonitorSelection.GpuDevices(metrics) && iteration++ % 30 == 0) { try { gpus = SystemMonitorNative.GetGpus(); } catch { } }
                bool collected = pdh.Sample();
                double? cpu = collected && !warmup && pdh.Read("cpu") is { } cpuValue ? SystemMonitorMetrics.Percentage(cpuValue) : null;
                double? frequency = collected ? pdh.Read("frequency") : null;
                var memory = new SystemMonitorNative.MemoryStatus { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<SystemMonitorNative.MemoryStatus>() };
                bool memoryOk = Has("Memory") && SystemMonitorNative.GetMemory(ref memory) && memory.Total > 0 && memory.Available <= memory.Total;
                var gpuLoads = collected && !warmup ? SystemMonitorMetrics.GpuLoad(pdh.ReadArray("gpu")
                    .Select(s => (Adapter: SystemMonitorMetrics.AdapterKey(s.Name), Engine: SystemMonitorMetrics.EngineKey(s.Name), s.Value))
                    .Where(s => s.Adapter is not null).Select(s => (s.Adapter!, s.Engine, s.Value))) : [];
                MonitorDevice? gpu = gpus.FirstOrDefault(g => g.Id == options.GpuId) ?? gpus.FirstOrDefault(g => g.DedicatedMemory > 0) ?? gpus.FirstOrDefault();
                double? gpuLoad = gpu is not null && gpuLoads.TryGetValue(gpu.Id, out double load) ? load : null;
                var vramSamples = collected && gpu is not null ? pdh.ReadArray("vram").Where(s => SystemMonitorMetrics.AdapterKey(s.Name) == gpu.Id).ToList() : [];
                // Adapter memory instances represent distinct physical segments, never process counters.
                double used = vramSamples.Sum(s => s.Value);
                ulong? vramUsed = vramSamples.Count > 0 && double.IsFinite(used) && used >= 0 && used < ulong.MaxValue ? (ulong)used : null;
                ulong? vramTotal = gpu is { DedicatedMemory: > 0 } ? gpu.DedicatedMemory : null;
                var interfaces = (SystemMonitorSelection.Network(metrics) ? NetworkInterface.GetAllNetworkInterfaces() : [])
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)).ToList();
                bool HasGateway(NetworkInterface network) { try { return network.GetIPProperties().GatewayAddresses.Count > 0; } catch { return false; } }
                var network = interfaces.FirstOrDefault(n => n.Id == options.NetworkId) ?? interfaces.FirstOrDefault(HasGateway) ?? interfaces.FirstOrDefault();
                long now = Stopwatch.GetTimestamp();
                string? address = null;
                if (network is not null && Has("IP"))
                {
                    try
                    {
                        var addresses = network.GetIPProperties().UnicastAddresses.Select(a => a.Address)
                            .Where(a => !System.Net.IPAddress.IsLoopback(a)).ToArray();
                        var ipv4 = addresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToArray();
                        address = string.Join("\n", (ipv4.Length > 0 ? ipv4 : addresses.Where(a => !a.IsIPv6LinkLocal).ToArray()).Select(a => a.ToString()).Distinct());
                        if (string.IsNullOrEmpty(address)) address = null;
                    }
                    catch { }
                }
                double? upload = null, download = null;
                bool networkRead = false;
                if (network is not null && SystemMonitorSelection.NetworkRates(metrics))
                {
                    try
                    {
                        var stats = network.GetIPStatistics();
                        networkRead = true;
                        if (previousNetwork == network.Id && previousTime > 0)
                        {
                            double seconds = Stopwatch.GetElapsedTime(previousTime, now).TotalSeconds;
                            if (Has("Upload")) upload = SystemMonitorMetrics.Rate(previousSent, stats.BytesSent, seconds);
                            if (Has("Download")) download = SystemMonitorMetrics.Rate(previousReceived, stats.BytesReceived, seconds);
                        }
                        previousSent = stats.BytesSent; previousReceived = stats.BytesReceived;
                    }
                    catch { previousTime = 0; }
                }
                else previousTime = 0;
                previousNetwork = network?.Id;
                previousTime = networkRead ? now : 0;
                bool fallback = (SystemMonitorSelection.GpuDevices(metrics) && !string.IsNullOrEmpty(options.GpuId) && gpu?.Id != options.GpuId) || (SystemMonitorSelection.Network(metrics) && !string.IsNullOrEmpty(options.NetworkId) && network?.Id != options.NetworkId);
                var gpuSensors = nvml?.Read(gpu, gpus, metrics) ?? new MonitorSensors();
                var cpuSensors = cpuBridge?.Read();
                var sensors = gpuSensors with { CpuTemperature = Has("CpuTemperature") ? cpuSensors?.Temperature : null, CpuPower = Has("CpuPower") ? cpuSensors?.Power : null };
                var snapshot = new SystemMonitorSnapshot(DateTimeOffset.Now, cpu, frequency,
                    memoryOk ? memory.Total - memory.Available : null, memoryOk ? memory.Total : null,
                    gpus.ToArray(), gpu?.Id, gpuLoad, vramUsed, vramTotal,
                    interfaces.Select(n => new MonitorDevice(n.Id, n.Name)).ToArray(), network?.Id,
                    upload, download, warmup, fallback, address, sensors);
                token.ThrowIfCancellationRequested();
                Updated?.Invoke(snapshot);
                warmup = !collected;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { previousNetwork = null; previousTime = 0; warmup = true; Failed?.Invoke(ex); }
            await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), token).ConfigureAwait(false);
        }
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _cancellation?.Cancel(); _cancellation = null; }
    }
}
