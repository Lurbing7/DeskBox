using DeskBox.Models;
using DeskBox.Platform;
using DeskBox.Services;
using Windows.Graphics;

namespace DeskBox.Tests;

public sealed class WidgetMonitorLayoutTests
{
    private static Win32Helper.MonitorWorkAreaInfo Screen(string name, bool primary, int x, double scale) => new(
        new Win32Helper.RECT { Left = x, Top = 0, Right = x + 1920, Bottom = 1080 },
        new Win32Helper.RECT { Left = x, Top = 0, Right = x + 1920, Bottom = 1040 }, name, primary, scale);

    [Fact]
    public void DockDefaultsToAllDisplays()
    {
        var screens = new[] { Screen("primary", true, 0, 1.5), Screen("secondary", false, -1920, 1) };
        Assert.Equal(2, WidgetMonitorLayout.DockScreens(new WidgetConfig(), screens).Count);
    }

    [Fact]
    public void SelectedDisplayFallsBackWithoutLosingPreference()
    {
        var primary = Screen("primary", true, 0, 1.5);
        var secondary = Screen("secondary", false, -1920, 1);
        var config = new WidgetConfig();
        config.Metadata["DockDisplayMode"] = "selected";
        config.Metadata["DockMonitorDevice"] = "secondary";
        Assert.Equal("primary", WidgetMonitorLayout.DockScreens(config, [primary]).Single().DeviceName);
        Assert.Equal("secondary", config.Metadata["DockMonitorDevice"]);
        Assert.Equal("secondary", WidgetMonitorLayout.DockScreens(config, [primary, secondary]).Single().DeviceName);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(8, 1.5)]
    public void TopAndBottomCenteredPositionsUseTheirOwnMonitor(int position, double scale)
    {
        var work = new RectInt32(-1920, -1080, 1920, 1040);
        var placed = WidgetMonitorLayout.Place(work, scale, position, 400, 64, 8);
        Assert.Equal(work.X + (work.Width - placed.Width) / 2, placed.X);
        Assert.Equal(position == 2 ? work.Y + (int)Math.Ceiling(8 * scale) : work.Y + work.Height - placed.Height - (int)Math.Ceiling(8 * scale), placed.Y);
    }

    [Fact]
    public void OversizedWidgetStaysInsideMonitor()
    {
        var work = new RectInt32(2560, 0, 640, 480);
        Assert.Equal(work, WidgetMonitorLayout.Place(work, 1.5, 6, 1000, 1000, 64));
    }

    [Theory]
    [InlineData(1, 1.25)]
    [InlineData(3, 1.5)]
    [InlineData(7, 1.5)]
    [InlineData(9, 1.25)]
    public void CornersAnchorTheCompleteWindowToBothEdges(int position, double scale)
    {
        var work = new RectInt32(-2560, -1440, 2560, 1400);
        var placed = WidgetMonitorLayout.Place(work, scale, position, 1200, 84, 12);
        int margin = (int)Math.Ceiling(12 * scale);
        Assert.Equal(position is 1 or 7 ? work.X + margin : work.X + work.Width - placed.Width - margin, placed.X);
        Assert.Equal(position is 1 or 3 ? work.Y + margin : work.Y + work.Height - placed.Height - margin, placed.Y);
    }
}
