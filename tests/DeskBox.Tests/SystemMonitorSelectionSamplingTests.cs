using DeskBox.Models;
using DeskBox.Services;
using Xunit;

namespace DeskBox.Tests;

public sealed class SystemMonitorSelectionSamplingTests
{
    [Fact]
    public async Task MemoryOnly_ProducesMemoryWithoutOtherSourcesAndStopsBeforeAllOffRestart()
    {
        int mask = 1 << Array.IndexOf(SystemMonitorSelection.Keys, "Memory");
        using var sampler = new SystemMonitorSampler(new(1, "", "", true, mask));
        var first = new TaskCompletionSource<SystemMonitorSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        sampler.Updated += snapshot => { Interlocked.Increment(ref count); first.TrySetResult(snapshot); };
        sampler.Start();
        var sample = await first.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(sample.MemoryTotal > 0); Assert.True(sample.MemoryUsed > 0);
        Assert.Null(sample.CpuLoad); Assert.Null(sample.GpuLoad); Assert.Null(sample.NetworkAddress);
        Assert.Empty(sample.Gpus); Assert.Empty(sample.Networks);
        Assert.Null(sample.Sensors?.CpuTemperature); Assert.Null(sample.Sensors?.GpuTemperature);
        sampler.Stop(); await sampler.Completion;
        int stoppedCount = count;
        sampler.Options = sampler.Options with { Metrics = 0 };
        sampler.Start(); await sampler.Completion.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(stoppedCount, count);
        var resumed = new TaskCompletionSource<SystemMonitorSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        sampler.Updated += snapshot => resumed.TrySetResult(snapshot);
        sampler.Options = sampler.Options with { Metrics = mask }; sampler.Start();
        var restarted = await resumed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(restarted.Warmup); Assert.True(restarted.Timestamp >= sample.Timestamp);
        sampler.Stop(); await sampler.Completion;
    }
}
