using DeskBox.Services;
using Xunit;

namespace DeskBox.Tests;

public sealed class ClockMonitorAdaptiveTests
{
    [Theory]
    [InlineData(0, 0, 0)] [InlineData(255, 255, 255)] [InlineData(128, 128, 128)]
    [InlineData(40, 25, 12)] [InlineData(240, 180, 30)] [InlineData(10, 100, 170)]
    public void ComplementGuard_ReadableOnFlatBackground(int r, int g, int b)
    {
        var pixel = new GlanceTextContrastPolicy.Rgb((byte)r, (byte)g, (byte)b);
        var result = GlanceTextContrastPolicy.Resolve([pixel]);
        Assert.True(GlanceTextContrastPolicy.Contrast(result.Foreground,
            GlanceTextContrastPolicy.Blend(new(0, 0, 0), pixel, result.BlackScrim)) >= 4.5);
    }
    [Fact]
    public void MixedImage_UsesLocalScrimForBothExtremes()
    {
        GlanceTextContrastPolicy.Rgb[] pixels = [new(0, 0, 0), new(128, 128, 128), new(255, 255, 255)];
        var result = GlanceTextContrastPolicy.Resolve(pixels);
        Assert.InRange(result.BlackScrim, .01, .7);
        Assert.All(pixels, pixel => Assert.True(GlanceTextContrastPolicy.Contrast(result.Foreground,
            GlanceTextContrastPolicy.Blend(new(0, 0, 0), pixel, result.BlackScrim)) >= 4.5));
    }
    [Fact]
    public void CropMapping_RespectsFillFocusAndFitLetterbox()
    {
        var center = GlanceTextContrastPolicy.ImagePoint(0, 0, 100, 100, 200, 100, false, .5, .5);
        var right = GlanceTextContrastPolicy.ImagePoint(0, 0, 100, 100, 200, 100, false, 1, .5);
        var fit = GlanceTextContrastPolicy.ImagePoint(50, 0, 100, 100, 200, 100, true, .5, .5);
        Assert.Equal(.25, center.X, 5); Assert.Equal(.5, right.X, 5); Assert.True(fit.Y < 0);
    }
    private static DateTimeOffset At(int day, int hour = 12) => new(new DateTime(2026, 10, day, hour, 0, 0, DateTimeKind.Local));
    [Fact]
    public void DailyPolicy_StartupCompletedDayAndSleepAcrossMidnight()
    {
        var policy = new GlanceDailyRefreshPolicy();
        Assert.True(policy.ShouldRefresh(At(6)));
        policy.Begin(At(6)); policy.Complete(At(6), new DateOnly(2026, 10, 6));
        Assert.False(policy.ShouldRefresh(At(6, 23)));
        Assert.True(policy.ShouldRefresh(At(8, 9)));
    }
    [Fact]
    public void DailyPolicy_OldCacheRetriesWithoutNetworkStorm()
    {
        var policy = new GlanceDailyRefreshPolicy(); var now = At(6);
        policy.Begin(now); policy.Complete(now, new DateOnly(2026, 10, 5));
        Assert.False(policy.ShouldRefresh(now.AddMinutes(14)));
        Assert.True(policy.ShouldRefresh(now.AddMinutes(15)));
        policy.Begin(now.AddMinutes(15)); policy.Reset(); Assert.True(policy.ShouldRefresh(now.AddMinutes(16)));
    }
    [Fact]
    public void DailyPolicy_VerifiedLatestCatalogCanHaveDifferentSourceDate()
    {
        var policy = new GlanceDailyRefreshPolicy(); var now = At(6);
        policy.Begin(now); policy.Complete(now, new DateOnly(2026, 10, 5), latestVerified: true);
        Assert.False(policy.ShouldRefresh(now.AddHours(2))); Assert.True(policy.ShouldRefresh(At(7)));
    }
    [Fact]
    public void DailyPolicy_HiddenCancellationRetriesOnRevealWithoutDiscardingCompletedDay()
    {
        var policy = new GlanceDailyRefreshPolicy(); var now = At(6);
        policy.Begin(now); policy.Cancel();
        Assert.True(policy.ShouldRefresh(now.AddSeconds(1)));
        policy.Begin(now); policy.Complete(now, new DateOnly(2026, 10, 6)); policy.Cancel();
        Assert.False(policy.ShouldRefresh(now.AddSeconds(2)));
    }
    [Fact]
    public void OldMonitorPreferences_EnableAllAndExplicitFalseDisables()
    {
        var metadata = new Dictionary<string, string>();
        Assert.Equal(SystemMonitorSelection.All, SystemMonitorSelection.Read(metadata));
        metadata["MonitorMetric.CpuTemperature"] = "false"; metadata["MonitorMetric.CpuPower"] = "false";
        int mask = SystemMonitorSelection.Read(metadata);
        Assert.False(SystemMonitorSelection.CpuSensors(mask)); Assert.True(SystemMonitorSelection.Has(mask, "CpuLoad"));
        Assert.True(SystemMonitorSelection.GpuSensors(mask));
    }
    [Fact]
    public void IpOnly_UsesNetworkCatalogWithoutRateCountersOrGpuOrCpuBridge()
    {
        int mask = 1 << Array.IndexOf(SystemMonitorSelection.Keys, "IP");
        Assert.True(SystemMonitorSelection.Network(mask)); Assert.False(SystemMonitorSelection.NetworkRates(mask));
        Assert.False(SystemMonitorSelection.CpuSensors(mask)); Assert.False(SystemMonitorSelection.GpuDevices(mask));
        Assert.False(SystemMonitorSelection.Network(0));
    }
    [Theory]
    [InlineData("Right")] [InlineData("Left")] [InlineData("Top")] [InlineData("Bottom")]
    [InlineData("TopLeft")] [InlineData("BottomRight")]
    public void AspectResize_PreservesRatioAndOppositeAnchor(string direction)
    {
        var initial = new SystemMonitorAspectLayout.Bounds(600, 300, 220, 420);
        var result = SystemMonitorAspectLayout.Resize(initial, new(400, 100, 320, 540), new(0, 0, 2000, 1400), direction, .5, 20, 20);
        Assert.InRange(Math.Abs((result.Width - 20) / (double)(result.Height - 20) - .5), 0, .002);
        if (direction.Contains("Left")) Assert.Equal(initial.X + initial.Width, result.X + result.Width);
        if (direction.Contains("Top")) Assert.Equal(initial.Y + initial.Height, result.Y + result.Height);
        if (direction.Contains("Right")) Assert.Equal(initial.X, result.X);
        if (direction.Contains("Bottom")) Assert.Equal(initial.Y, result.Y);
    }
    [Fact]
    public void AspectResize_ClampsToScreenWithNegativeOrigin()
    {
        var work = new SystemMonitorAspectLayout.Bounds(-1920, 0, 1920, 1080);
        var result = SystemMonitorAspectLayout.Resize(new(-500, 100, 320, 600), new(-500, 100, 2000, 1800), work, "BottomRight", .5);
        Assert.True(result.X >= work.X && result.X + result.Width <= 0);
        Assert.True(result.Y >= 0 && result.Y + result.Height <= 1080);
        Assert.InRange(Math.Abs(result.Width / (double)result.Height - .5), 0, .002);
    }
}
