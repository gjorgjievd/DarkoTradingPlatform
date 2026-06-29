using TradingAgent.DTOs;

namespace TradingAgent.Services.Market;

public interface IMarketProvider
{
    string MarketName { get; }
    MarketStatusDto GetStatus(DateTime utcNow);
}

public interface IMarketCalendarService
{
    IReadOnlyList<MarketHolidayDto> GetHolidays(string market, int year);
    bool IsHoliday(string market, DateOnly date);
}

public interface IMarketStatusService
{
    MarketStatusDto GetStatus(string? market = null, DateTime? utcNow = null);
    IReadOnlyList<MarketHolidayDto> GetCalendar(string? market = null, int? year = null);
    bool ShouldIgnoreSignal(MarketStatusDto status);
    string GetIgnoredReason(MarketStatusDto status);
}
