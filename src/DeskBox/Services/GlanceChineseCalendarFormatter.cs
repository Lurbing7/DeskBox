using System.Globalization;

namespace DeskBox.Services;

internal static class GlanceChineseCalendarFormatter
{
    private static readonly string[] MonthNames =
        ["", "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月"];
    private static readonly string[] DayNames =
    [
        "", "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
    ];
    private static readonly string[] HeavenlyStems = ["甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"];
    private static readonly string[] EarthlyBranches = ["子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"];

    public static string FormatDay(DateOnly date)
    {
        var value = GetDate(date);
        return value.Day == 1 ? FormatMonth(value) : DayNames[value.Day];
    }

    public static string FormatTitle(DateOnly date)
    {
        var value = GetDate(date);
        return $"{FormatYear(value)} {FormatMonth(value)}{DayNames[value.Day]}";
    }

    public static string FormatClockDate(DateOnly date, bool includeYear)
    {
        string solar = date.ToString(includeYear ? "yyyy年M月d日" : "M月d日", CultureInfo.InvariantCulture);
        try
        {
            var value = GetDate(date);
            string lunar = $"{(includeYear ? FormatYear(value) : string.Empty)}{FormatMonth(value)}{DayNames[value.Day]}";
            return $"{solar} / {lunar}";
        }
        catch (ArgumentOutOfRangeException)
        {
            // Outside the system calendar range, keep the usable Gregorian date.
            return solar;
        }
    }

    private static string FormatMonth(ChineseDate value) =>
        $"{(value.IsLeapMonth ? "闰" : string.Empty)}{MonthNames[value.Month]}";

    private static string FormatYear(ChineseDate value) =>
        $"{HeavenlyStems[(value.SexagenaryYear - 1) % 10]}{EarthlyBranches[(value.SexagenaryYear - 1) % 12]}年";

    private static ChineseDate GetDate(DateOnly date)
    {
        var calendar = new ChineseLunisolarCalendar();
        DateTime value = date.ToDateTime(TimeOnly.MinValue);
        int year = calendar.GetYear(value);
        int calendarMonth = calendar.GetMonth(value);
        int leapMonth = calendar.GetLeapMonth(year);
        bool isLeapMonth = leapMonth > 0 && calendarMonth == leapMonth;
        int month = leapMonth > 0 && calendarMonth >= leapMonth ? calendarMonth - 1 : calendarMonth;
        return new ChineseDate(month, calendar.GetDayOfMonth(value), isLeapMonth, calendar.GetSexagenaryYear(value));
    }

    private readonly record struct ChineseDate(int Month, int Day, bool IsLeapMonth, int SexagenaryYear);
}
