using TradingAgent.DTOs;

namespace TradingAgent.Services.Market;

internal static class UsEquityMarketEvaluator
{
    private static readonly TimeSpan RegularOpen = new(9, 30, 0);
    private static readonly TimeSpan RegularClose = new(16, 0, 0);
    private static readonly TimeSpan PreMarketOpen = new(4, 0, 0);
    private static readonly TimeSpan AfterHoursClose = new(20, 0, 0);

    public static MarketStatusDto Evaluate(string marketName, string timeZoneId, DateTime utcNow, IMarketCalendarService calendar)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var marketTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
        var marketDate = DateOnly.FromDateTime(marketTime);
        var timeOfDay = marketTime.TimeOfDay;

        var isWeekend = marketTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var isHoliday = calendar.IsHoliday(marketName, marketDate);

        string session;
        bool isOpen;
        bool isPreMarket = false;
        bool isAfterHours = false;
        bool isOvernight = false;
        string? reason;

        if (isHoliday)
        {
            session = MarketSessionValues.Holiday;
            isOpen = false;
            reason = "US market holiday";
        }
        else if (isWeekend)
        {
            session = MarketSessionValues.Weekend;
            isOpen = false;
            reason = "Weekend";
        }
        else if (timeOfDay >= RegularOpen && timeOfDay < RegularClose)
        {
            session = MarketSessionValues.Regular;
            isOpen = true;
            reason = "Regular trading hours";
        }
        else if (timeOfDay >= PreMarketOpen && timeOfDay < RegularOpen)
        {
            session = MarketSessionValues.PreMarket;
            isOpen = false;
            isPreMarket = true;
            reason = "Pre-market session";
        }
        else if (timeOfDay >= RegularClose && timeOfDay < AfterHoursClose)
        {
            session = MarketSessionValues.AfterHours;
            isOpen = false;
            isAfterHours = true;
            reason = "After-hours session";
        }
        else
        {
            session = MarketSessionValues.Overnight;
            isOpen = false;
            isOvernight = true;
            reason = "Overnight session";
        }

        var nextOpenUtc = FindNextRegularOpenUtc(marketName, timeZone, marketTime, calendar);
        var nextCloseUtc = FindNextCloseUtc(timeZone, marketTime, session, calendar, marketName);

        return new MarketStatusDto
        {
            MarketName = marketName,
            TimeZone = timeZoneId,
            Status = session,
            MarketSession = session,
            IsOpen = isOpen,
            IsWeekend = isWeekend,
            IsHoliday = isHoliday,
            IsPreMarket = isPreMarket,
            IsAfterHours = isAfterHours,
            IsOvernight = isOvernight,
            CurrentMarketTime = marketTime,
            CheckedAtUtc = utcNow,
            NextOpenTimeUtc = nextOpenUtc,
            NextCloseTimeUtc = nextCloseUtc,
            Reason = reason
        };
    }

    private static DateTime? FindNextRegularOpenUtc(
        string marketName,
        TimeZoneInfo timeZone,
        DateTime marketTime,
        IMarketCalendarService calendar)
    {
        var candidateDate = DateOnly.FromDateTime(marketTime);
        var candidateTime = marketTime.TimeOfDay;

        if (candidateTime < RegularOpen
            && !calendar.IsHoliday(marketName, candidateDate)
            && candidateDate.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
        {
            return ToUtc(candidateDate, RegularOpen, timeZone);
        }

        candidateDate = candidateDate.AddDays(1);
        for (var i = 0; i < 366; i++)
        {
            if (candidateDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                || calendar.IsHoliday(marketName, candidateDate))
            {
                candidateDate = candidateDate.AddDays(1);
                continue;
            }

            return ToUtc(candidateDate, RegularOpen, timeZone);
        }

        return null;
    }

    private static DateTime? FindNextCloseUtc(
        TimeZoneInfo timeZone,
        DateTime marketTime,
        string session,
        IMarketCalendarService calendar,
        string marketName)
    {
        var marketDate = DateOnly.FromDateTime(marketTime);

        if (session == MarketSessionValues.Regular
            && !calendar.IsHoliday(marketName, marketDate)
            && marketDate.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
        {
            return ToUtc(marketDate, RegularClose, timeZone);
        }

        if (session == MarketSessionValues.PreMarket
            && !calendar.IsHoliday(marketName, marketDate)
            && marketDate.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
        {
            return ToUtc(marketDate, RegularClose, timeZone);
        }

        return null;
    }

    private static DateTime ToUtc(DateOnly date, TimeSpan time, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.FromTimeSpan(time)), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);
    }
}
