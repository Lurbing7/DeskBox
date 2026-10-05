using System.Globalization;
using LibreHardwareMonitor.Hardware;

// The sensor dependency stays in a JIT sidecar rather than the WinUI/AOT process.
// This component only reads CPU sensors and never installs drivers or controls hardware.
var computer = new Computer { IsCpuEnabled = true };
using var cancellation = new CancellationTokenSource();
_ = Task.Run(async () => { await Console.In.ReadToEndAsync(); cancellation.Cancel(); });
try
{
    computer.Open();
    while (!cancellation.IsCancellationRequested)
    {
        double? temperature = null, power = null;
        foreach (var cpu in computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
        {
            cpu.Update();
            var temperatures = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0 and < 150).ToArray();
            var preferred = temperatures.FirstOrDefault(s => s.Name.Contains("Tctl/Tdie", StringComparison.OrdinalIgnoreCase))
                ?? temperatures.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                ?? temperatures.FirstOrDefault(s => s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase));
            temperature = preferred?.Value;
            var package = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Equals("Package", StringComparison.OrdinalIgnoreCase));
            if (package?.Value is >= 0 and < 2000) power = package.Value;
        }
        Console.WriteLine($"{Format(temperature)}\t{Format(power)}");
        await Task.Delay(2000, cancellation.Token);
    }
}
catch (OperationCanceledException) { }
catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name); }
finally { computer.Close(); }
static string Format(double? value) => value is { } number && double.IsFinite(number) ? number.ToString("R", CultureInfo.InvariantCulture) : "-";
