using Microsoft.Extensions.Logging.Abstractions;
using TradingAgent.Configuration;
using TradingAgent.DTOs;
using TradingAgent.Services;
using TradingAgent.Services.Market;
using Xunit;

namespace TradingAgent.Tests;

public class MarketCalendarServiceTests
{
    private readonly MarketCalendarService _calendar = new();

    [Fact]
    public void NewYearsDay_2026_IsThursday_NotObserved()
    {
        var holidays = _calendar.GetHolidays(MarketNames.Nasdaq, 2026);
        var newYears = holidays.Single(holiday => holiday.Name == "New Year's Day");

        Assert.Equal(new DateOnly(2026, 1, 1), newYears.Date);
        Assert.Equal(new DateOnly(2026, 1, 1), newYears.ObservedDate);
        Assert.False(newYears.IsObserved);
    }

    [Fact]
    public void IndependenceDay_2026_ObservedOnFriday_WhenJuly4IsSaturday()
    {
        var holidays = _calendar.GetHolidays(MarketNames.Nasdaq, 2026);
        var independenceDay = holidays.Single(holiday => holiday.Name == "Independence Day");

        Assert.Equal(new DateOnly(2026, 7, 4), independenceDay.Date);
        Assert.Equal(new DateOnly(2026, 7, 3), independenceDay.ObservedDate);
        Assert.True(independenceDay.IsObserved);
    }

    [Fact]
    public void Christmas_2027_ObservedOnMonday_WhenDec25IsSaturday()
    {
        var holidays = _calendar.GetHolidays(MarketNames.Nasdaq, 2027);
        var christmas = holidays.Single(holiday => holiday.Name == "Christmas Day");

        Assert.Equal(new DateOnly(2027, 12, 25), christmas.Date);
        Assert.Equal(new DateOnly(2027, 12, 24), christmas.ObservedDate);
        Assert.True(christmas.IsObserved);
    }

    [Fact]
    public void GoodFriday_2026_IsApril3()
    {
        var holidays = _calendar.GetHolidays(MarketNames.Nasdaq, 2026);
        var goodFriday = holidays.Single(holiday => holiday.Name == "Good Friday");

        Assert.Equal(new DateOnly(2026, 4, 3), goodFriday.ObservedDate);
    }
}

public class UsEquityMarketStatusTests
{
    private readonly MarketCalendarService _calendar = new();
    private readonly NasdaqMarketProvider _provider;

    public UsEquityMarketStatusTests()
    {
        _provider = new NasdaqMarketProvider(_calendar);
    }

    [Fact]
    public void RegularSession_TuesdayMidday()
    {
        var utc = new DateTime(2026, 6, 23, 16, 0, 0, DateTimeKind.Utc);
        var status = _provider.GetStatus(utc);

        Assert.Equal(MarketSessionValues.Regular, status.MarketSession);
        Assert.True(status.IsOpen);
        Assert.False(status.IsWeekend);
        Assert.False(status.IsHoliday);
    }

    [Fact]
    public void Weekend_Saturday()
    {
        var utc = new DateTime(2026, 6, 27, 15, 0, 0, DateTimeKind.Utc);
        var status = _provider.GetStatus(utc);

        Assert.Equal(MarketSessionValues.Weekend, status.MarketSession);
        Assert.True(status.IsWeekend);
        Assert.False(status.IsOpen);
    }

    [Fact]
    public void Holiday_NewYearsDay()
    {
        var utc = new DateTime(2026, 1, 1, 15, 0, 0, DateTimeKind.Utc);
        var status = _provider.GetStatus(utc);

        Assert.Equal(MarketSessionValues.Holiday, status.MarketSession);
        Assert.True(status.IsHoliday);
        Assert.False(status.IsOpen);
    }

    [Fact]
    public void PreMarket_WeekdayBeforeOpen()
    {
        var utc = new DateTime(2026, 6, 23, 12, 0, 0, DateTimeKind.Utc);
        var status = _provider.GetStatus(utc);

        Assert.Equal(MarketSessionValues.PreMarket, status.MarketSession);
        Assert.True(status.IsPreMarket);
        Assert.False(status.IsOpen);
    }

    [Fact]
    public void AfterHours_WeekdayAfterClose()
    {
        var utc = new DateTime(2026, 6, 23, 21, 0, 0, DateTimeKind.Utc);
        var status = _provider.GetStatus(utc);

        Assert.Equal(MarketSessionValues.AfterHours, status.MarketSession);
        Assert.True(status.IsAfterHours);
        Assert.False(status.IsOpen);
    }

    [Fact]
    public void Overnight_WeekdayLateNight()
    {
        var utc = new DateTime(2026, 6, 24, 2, 0, 0, DateTimeKind.Utc);
        var status = _provider.GetStatus(utc);

        Assert.Equal(MarketSessionValues.Overnight, status.MarketSession);
        Assert.True(status.IsOvernight);
        Assert.False(status.IsOpen);
    }

    [Fact]
    public void ObservedHoliday_IndependenceDay2026()
    {
        var utc = new DateTime(2026, 7, 3, 15, 0, 0, DateTimeKind.Utc);
        var status = _provider.GetStatus(utc);

        Assert.Equal(MarketSessionValues.Holiday, status.MarketSession);
        Assert.True(status.IsHoliday);
    }

    [Fact]
    public void DstSafe_NewYorkTimeConversion()
    {
        var winterUtc = new DateTime(2026, 1, 5, 15, 0, 0, DateTimeKind.Utc);
        var summerUtc = new DateTime(2026, 7, 6, 14, 0, 0, DateTimeKind.Utc);

        var winterStatus = _provider.GetStatus(winterUtc);
        var summerStatus = _provider.GetStatus(summerUtc);

        Assert.Equal(10, winterStatus.CurrentMarketTime.Hour);
        Assert.Equal(10, summerStatus.CurrentMarketTime.Hour);
        Assert.Equal(MarketSessionValues.Regular, winterStatus.MarketSession);
        Assert.Equal(MarketSessionValues.Regular, summerStatus.MarketSession);
    }
}

public class MarketStatusServiceTests
{
    private readonly MarketCalendarService _calendar = new();
    private readonly IReadOnlyList<IMarketProvider> _providers =
    [
        new NasdaqMarketProvider(new MarketCalendarService()),
        new NyseMarketProvider(new MarketCalendarService())
    ];

    [Fact]
    public void Enable24_5_AllowsExtendedSessions()
    {
        var settings = new AppSettings { Enable24_5Trading = true, IgnoreSignalsWhenMarketClosed = true };
        var service = new MarketStatusService(_providers, _calendar, settings, NullLogger<MarketStatusService>.Instance);

        var preMarket = service.GetStatus(MarketNames.Nasdaq, new DateTime(2026, 6, 23, 12, 0, 0, DateTimeKind.Utc));
        var overnight = service.GetStatus(MarketNames.Nasdaq, new DateTime(2026, 6, 24, 2, 0, 0, DateTimeKind.Utc));

        Assert.False(service.ShouldIgnoreSignal(preMarket));
        Assert.False(service.ShouldIgnoreSignal(overnight));
        Assert.Equal(70, preMarket.SessionConfidenceThreshold);
        Assert.Equal(75, overnight.SessionConfidenceThreshold);
    }

    [Fact]
    public void Disable24_5_RequiresSessionFlags()
    {
        var settings = new AppSettings
        {
            Enable24_5Trading = false,
            AllowPreMarket = false,
            AllowAfterHours = false,
            AllowOvernight = false,
            IgnoreSignalsWhenMarketClosed = true
        };
        var service = new MarketStatusService(_providers, _calendar, settings, NullLogger<MarketStatusService>.Instance);

        var preMarket = service.GetStatus(MarketNames.Nasdaq, new DateTime(2026, 6, 23, 12, 0, 0, DateTimeKind.Utc));
        var regular = service.GetStatus(MarketNames.Nasdaq, new DateTime(2026, 6, 23, 16, 0, 0, DateTimeKind.Utc));

        Assert.True(service.ShouldIgnoreSignal(preMarket));
        Assert.False(service.ShouldIgnoreSignal(regular));
    }

    [Fact]
    public void WeekendAndHoliday_AlwaysIgnored()
    {
        var settings = new AppSettings { Enable24_5Trading = true, IgnoreSignalsWhenMarketClosed = true };
        var service = new MarketStatusService(_providers, _calendar, settings, NullLogger<MarketStatusService>.Instance);

        var weekend = service.GetStatus(MarketNames.Nasdaq, new DateTime(2026, 6, 27, 15, 0, 0, DateTimeKind.Utc));
        var holiday = service.GetStatus(MarketNames.Nasdaq, new DateTime(2026, 1, 1, 15, 0, 0, DateTimeKind.Utc));

        Assert.True(service.ShouldIgnoreSignal(weekend));
        Assert.True(service.ShouldIgnoreSignal(holiday));
    }
}

public class NotificationFilterTests
{
    [Fact]
    public void UsesSessionConfidenceThreshold()
    {
        var settings = new AppSettings { SendIgnoredSignals = false, MinConfidenceRegular = 70 };
        var marketStatus = new MarketStatusDto
        {
            MarketSession = MarketSessionValues.PreMarket,
            SessionConfidenceThreshold = 85
        };
        var response = new ClaudeAnalysisResponse
        {
            Analysis = new ClaudeAnalysisResult
            {
                Action = "BUY",
                Confidence = 80,
                ShouldNotify = true
            }
        };

        Assert.False(NotificationFilter.ShouldSendTelegram(settings, response, marketStatus));

        response.Analysis!.Confidence = 85;
        Assert.True(NotificationFilter.ShouldSendTelegram(settings, response, marketStatus));
    }
}
