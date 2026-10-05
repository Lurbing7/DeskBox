using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DeskBox.Services;

internal sealed class SystemMonitorCpuBridge : IDisposable
{
    private readonly Process? _process;
    private sealed record Reading(double? Temperature, double? Power, long Timestamp);
    private Reading? _latest;
    public SystemMonitorCpuBridge()
    {
        try
        {
            string host = Path.Combine(AppContext.BaseDirectory, "SensorHost", "DeskBox.SensorHost.dll");
            string dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
            if (!File.Exists(host) || !File.Exists(dotnet)) return;
            var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(host);
            _process = Process.Start(start);
            if (_process is null) return;
            _ = ReadOutput(_process);
            _ = _process.StandardError.ReadToEndAsync();
        }
        catch { }
    }
    private async Task ReadOutput(Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > 128) continue;
                string[] fields = line.Split('\t');
                if (fields.Length != 2) continue;
                Volatile.Write(ref _latest, new(Parse(fields[0], 150), Parse(fields[1], 2000, allowZero: true), Stopwatch.GetTimestamp()));
            }
        }
        catch { }
        finally { Volatile.Write(ref _latest, null); }
    }
    private static double? Parse(string text, double maximum, bool allowZero = false) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) && (allowZero ? value >= 0 : value > 0) && value < maximum ? value : null;
    public (double? Temperature, double? Power) Read()
    {
        var latest = Volatile.Read(ref _latest);
        return latest is not null && Stopwatch.GetElapsedTime(latest.Timestamp).TotalSeconds < 6 ? (latest.Temperature, latest.Power) : (null, null);
    }
    public void Dispose()
    {
        if (_process is null) return;
        try
        {
            _process.StandardInput.Close();
            if (!_process.WaitForExit(300)) _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process.Dispose();
        Volatile.Write(ref _latest, null);
    }
}
