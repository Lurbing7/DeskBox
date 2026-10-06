using DeskBox.Services;
using Xunit;

namespace DeskBox.Tests;

public sealed class GlanceChineseClockDateTests
{
    [Theory]
    [InlineData(2026, 10, 6, "2026年10月6日 / 丙午年八月廿六")]
    [InlineData(2026, 1, 1, "2026年1月1日 / 乙巳年冬月十三")]
    [InlineData(2024, 2, 10, "2024年2月10日 / 甲辰年正月初一")]
    [InlineData(2025, 7, 25, "2025年7月25日 / 乙巳年闰六月初一")]
    [InlineData(2025, 8, 23, "2025年8月23日 / 乙巳年七月初一")]
    public void CompleteDate_RespectsLunarYearAndLeapMonth(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, GlanceChineseCalendarFormatter.FormatClockDate(new(year, month, day), includeYear: true));
    }

    [Fact]
    public void YearHidden_OmitsBothCalendarYears()
    {
        Assert.Equal("10月6日 / 八月廿六", GlanceChineseCalendarFormatter.FormatClockDate(new(2026, 10, 6), includeYear: false));
    }

    [Theory]
    [InlineData(1900, 1, 1)]
    [InlineData(2200, 1, 1)]
    public void UnsupportedLunarDate_FallsBackWithoutEmptySeparator(int year, int month, int day)
    {
        Assert.Equal($"{year}年{month}月{day}日", GlanceChineseCalendarFormatter.FormatClockDate(new(year, month, day), includeYear: true));
    }

    [Fact]
    public void CalendarGrid_KeepsExistingDayAndHeaderPresentation()
    {
        Assert.Equal("闰六月", GlanceChineseCalendarFormatter.FormatDay(new(2025, 7, 25)));
        Assert.Equal("廿六", GlanceChineseCalendarFormatter.FormatDay(new(2026, 10, 6)));
        Assert.Equal("丙午年 八月廿六", GlanceChineseCalendarFormatter.FormatTitle(new(2026, 10, 6)));
    }
}
