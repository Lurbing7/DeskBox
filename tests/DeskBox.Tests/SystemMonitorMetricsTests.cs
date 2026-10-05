using DeskBox.Services;
namespace DeskBox.Tests;

public sealed class SystemMonitorMetricsTests
{
    [Theory]
    [InlineData(100, 200, 2, 50)]
    [InlineData(0, 0, 1, 0)]
    public void NetworkRateUsesElapsedTime(long previous, long current, double seconds, double expected) =>
        Assert.Equal(expected, SystemMonitorMetrics.Rate(previous, current, seconds));
    [Fact]
    public void CounterResetsAndMissingTimeAreUnavailable()
    {
        Assert.Null(SystemMonitorMetrics.Rate(200, 100, 1));
        Assert.Null(SystemMonitorMetrics.Rate(100, 200, 0));
        Assert.Null(SystemMonitorMetrics.Percentage(double.NaN));
    }
    [Fact]
    public void GpuCombinesProcessesWithinEngineThenUsesBusiestEngine()
    {
        var result = SystemMonitorMetrics.GpuLoad(new[] { ("a", "3d", 20d), ("a", "3d", 30d), ("a", "copy", 40d), ("b", "3d", 12d) });
        Assert.Equal(50, result["a"]); Assert.Equal(12, result["b"]);
    }
    [Fact]
    public void InstanceIdentityExcludesProcessButIncludesPhysicalEngine()
    {
        string name = "pid_12_luid_0x00000000_0x00001234_phys_0_eng_1_engtype_3D";
        Assert.Equal("0x00000000_0x00001234", SystemMonitorMetrics.AdapterKey(name));
        Assert.Equal("_phys_0_eng_1_engtype_3D", SystemMonitorMetrics.EngineKey(name));
    }
}
