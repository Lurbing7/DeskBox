using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class DockManualOrderTests
{
    [Fact]
    public void MovingAnExistingEntryUsesTheOriginalDropIndex()
    {
        Assert.Equal("B|C|A", DockManualOrder.Insert(["A", "B", "C"], ["A"], 3));
        Assert.Equal("C|A|B", DockManualOrder.Insert(["A", "B", "C"], ["C"], 0));
        Assert.Equal("A|B|C", DockManualOrder.Insert(["A", "B", "C"], ["B"], 2));
    }

    [Fact]
    public void MultipleIncomingEntriesKeepTheirRelativeOrder()
    {
        Assert.Equal("A|X|Y|B", DockManualOrder.Insert(["A", "B"], ["X", "Y"], 1));
    }

    [Fact]
    public void RefreshKeepsManualOrderAndIgnoresRemovedOrDuplicateEntries()
    {
        Assert.Equal(new[] { "C", "A", "B" }, DockManualOrder.Arrange(["A", "B", "C"], "C|missing|C|A"));
    }
}
