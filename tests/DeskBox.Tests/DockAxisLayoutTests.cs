using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class DockAxisLayoutTests
{
    [Theory]
    [InlineData(4, true)]
    [InlineData(6, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    public void OnlySideCentersAreVertical(int position, bool vertical) => Assert.Equal(vertical, DockAxisLayout.Vertical(position));

    [Fact]
    public void VerticalInsertionPreservesHorizontalStoredOrder()
    {
        // Stored A,B,C is displayed C,B,A from top to bottom.
        Assert.Equal(3, DockAxisLayout.StorageInsertion(0, 3, true));
        Assert.Equal(2, DockAxisLayout.StorageInsertion(1, 3, true));
        Assert.Equal(1, DockAxisLayout.StorageInsertion(2, 3, true));
        Assert.Equal(0, DockAxisLayout.StorageInsertion(3, 3, true));
        Assert.Equal(1, DockAxisLayout.StorageInsertion(1, 3, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerticalFitIncludesEveryUnscaledRunningStateRow(bool names)
    {
        var config = new WidgetConfig();
        config.Metadata["DockPosition"] = "6";
        config.Metadata["DockShowNames"] = names ? "true" : "false";
        Assert.Equal(DockVisualLayout.ItemSize(config) + 16, DockAxisLayout.CrossSize(config));
        const int attention = 2, pinned = 32, running = 3, commands = 1;
        const double available = 600;
        int count = attention + pinned + running;
        double natural = DockAxisLayout.Length(config, attention, pinned, running, commands);
        double scalable = count * (DockAxisLayout.Slot(config) - 4) +
            (attention - 1 + pinned - 1 + running - 1) * DockVisualLayout.ItemSpacing(config);
        double factor = DockAxisLayout.Scale(config, available, attention, pinned, running, commands);
        Assert.InRange(factor, 0, 1);
        Assert.Equal(available, natural - scalable + scalable * factor, 6);
    }
}
