using TradingAgent.DTOs;

namespace TradingAgent.Services.Market;

public sealed class MarketCalendarService : IMarketCalendarService
{
    private static readonly string[] UsEquityMarkets = [MarketNames.Nasdaq, MarketNames.Nyse];

    public IReadOnlyList<MarketHolidayDto> GetHolidays(string market, int year)
    {
        if (!IsUsEquityMarket(market))
        {
            return [];
        }

        return
        [
            CreateFixedHoliday("New Year's Day", new DateOnly(year, 1, 1)),
            CreateFloatingHoliday("Martin Luther King Jr. Day", year, 1, DayOfWeek.Monday, 3),
            CreateFloatingHoliday("Presidents Day", year, 2, DayOfWeek.Monday, 3),
            CreateGoodFriday(year),
            CreateFloatingHoliday("Memorial Day", year, 5, DayOfWeek.Monday, -1),
            CreateFixedHoliday("Juneteenth", new DateOnly(year, 6, 19)),
            CreateFixedHoliday("Independence Day", new DateOnly(year, 7, 4)),
            CreateFloatingHoliday("Labor Day", year, 9, DayOfWeek.Monday, 1),
            CreateFloatingHoliday("Thanksgiving Day", year, 11, DayOfWeek.Thursday, 4),
            CreateFixedHoliday("Christmas Day", new DateOnly(year, 12, 25))
        ];
    }

    public bool IsHoliday(string market, DateOnly date)
    {
        if (!IsUsEquityMarket(market))
        {
            return false;
        }

        return GetHolidays(market, date.Year).Any(holiday => holiday.ObservedDate == date);
    }

    internal static DateOnly GetObservedDate(DateOnly holidayDate)
    {
        return holidayDate.DayOfWeek switch
        {
            DayOfWeek.Saturday => holidayDate.AddDays(-1),
            DayOfWeek.Sunday => holidayDate.AddDays(1),
            _ => holidayDate
        };
    }

    private static bool IsUsEquityMarket(string market)
        => UsEquityMarkets.Contains(market.Trim().ToUpperInvariant());

    private static MarketHolidayDto CreateFixedHoliday(string name, DateOnly date)
    {
        var observed = GetObservedDate(date);
        return new MarketHolidayDto
        {
            Name = name,
            Date = date,
            ObservedDate = observed,
            IsObserved = observed != date
        };
    }

    private static MarketHolidayDto CreateFloatingHoliday(
        string name,
        int year,
        int month,
        DayOfWeek dayOfWeek,
        int occurrence)
    {
        var date = occurrence > 0
            ? GetNthWeekdayOfMonth(year, month, dayOfWeek, occurrence)
            : GetLastWeekdayOfMonth(year, month, dayOfWeek);

        return new MarketHolidayDto
        {
            Name = name,
            Date = date,
            ObservedDate = date,
            IsObserved = false
        };
    }

    private static MarketHolidayDto CreateGoodFriday(int year)
    {
        var easter = CalculateEasterSunday(year);
        var goodFriday = easter.AddDays(-2);
        return new MarketHolidayDto
        {
            Name = "Good Friday",
            Date = goodFriday,
            ObservedDate = goodFriday,
            IsObserved = false
        };
    }

    internal static DateOnly CalculateEasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    private static DateOnly GetNthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int occurrence)
    {
        var date = new DateOnly(year, month, 1);
        while (date.DayOfWeek != dayOfWeek)
        {
            date = date.AddDays(1);
        }

        return date.AddDays(7 * (occurrence - 1));
    }

    private static DateOnly GetLastWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek)
    {
        var date = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        while (date.DayOfWeek != dayOfWeek)
        {
            date = date.AddDays(-1);
        }

        return date;
    }
}
