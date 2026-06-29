using TradingAgent.Configuration;
using TradingAgent.DTOs;

namespace TradingAgent.Services.Market;

public sealed class MarketStatusService(
    IEnumerable<IMarketProvider> providers,
    IMarketCalendarService calendar,
    AppSettings settings,
    ILogger<MarketStatusService> logger) : IMarketStatusService
{
    private readonly Dictionary<string, IMarketProvider> _providers = providers.ToDictionary(
        provider => provider.MarketName,
        StringComparer.OrdinalIgnoreCase);

    public MarketStatusDto GetStatus(string? market = null, DateTime? utcNow = null)
    {
        var marketName = NormalizeMarket(market ?? settings.MarketProvider);
        var atUtc = utcNow ?? DateTime.UtcNow;

        if (!_providers.TryGetValue(marketName, out var provider))
        {
            logger.LogWarning("Unknown market provider {Market}. Falling back to configured provider.", marketName);
            marketName = NormalizeMarket(settings.MarketProvider);
            provider = _providers[marketName];
        }

        var status = Enrich(provider.GetStatus(atUtc));

        logger.LogInformation(
            "Market status checked. Market={Market}, Session={Session}, IsOpen={IsOpen}, Threshold={Threshold}, MarketTime={MarketTime}",
            status.MarketName,
            status.MarketSession,
            status.IsOpen,
            status.SessionConfidenceThreshold,
            status.CurrentMarketTime);

        return status;
    }

    public IReadOnlyList<MarketHolidayDto> GetCalendar(string? market = null, int? year = null)
    {
        var marketName = NormalizeMarket(market ?? settings.MarketProvider);
        return calendar.GetHolidays(marketName, year ?? DateTime.UtcNow.Year);
    }

    public bool ShouldIgnoreSignal(MarketStatusDto status)
    {
        if (!settings.IgnoreSignalsWhenMarketClosed)
        {
            return false;
        }

        if (status.IsPlaceholder)
        {
            return true;
        }

        var session = status.MarketSession;

        if (session is MarketSessionValues.Weekend or MarketSessionValues.Holiday)
        {
            return true;
        }

        if (settings.Enable24_5Trading)
        {
            return session is not (
                MarketSessionValues.Regular
                or MarketSessionValues.PreMarket
                or MarketSessionValues.AfterHours
                or MarketSessionValues.Overnight);
        }

        return session switch
        {
            MarketSessionValues.Regular => false,
            MarketSessionValues.PreMarket => !settings.AllowPreMarket,
            MarketSessionValues.AfterHours => !settings.AllowAfterHours,
            MarketSessionValues.Overnight => !settings.AllowOvernight,
            _ => true
        };
    }

    public string GetIgnoredReason(MarketStatusDto status)
    {
        if (status.IsPlaceholder)
        {
            return status.Reason ?? "Market provider not implemented";
        }

        return status.MarketSession switch
        {
            MarketSessionValues.Holiday => "Holiday",
            MarketSessionValues.Weekend => "Weekend",
            MarketSessionValues.PreMarket => "Pre-market (not allowed)",
            MarketSessionValues.AfterHours => "After-hours (not allowed)",
            MarketSessionValues.Overnight => "Overnight (not allowed)",
            MarketSessionValues.Closed => "Market closed",
            _ => status.Reason ?? status.MarketSession
        };
    }

    private MarketStatusDto Enrich(MarketStatusDto status)
    {
        var threshold = MarketSessionConfidence.GetThreshold(settings, status.MarketSession);
        return new MarketStatusDto
        {
            MarketName = status.MarketName,
            TimeZone = status.TimeZone,
            Status = status.MarketSession,
            MarketSession = status.MarketSession,
            SessionConfidenceThreshold = threshold,
            IsOpen = status.IsOpen,
            IsWeekend = status.IsWeekend,
            IsHoliday = status.IsHoliday,
            IsPreMarket = status.IsPreMarket,
            IsAfterHours = status.IsAfterHours,
            IsOvernight = status.IsOvernight,
            CurrentMarketTime = status.CurrentMarketTime,
            CheckedAtUtc = status.CheckedAtUtc,
            NextOpenTimeUtc = status.NextOpenTimeUtc,
            NextCloseTimeUtc = status.NextCloseTimeUtc,
            Reason = status.Reason,
            IsPlaceholder = status.IsPlaceholder
        };
    }

    private static string NormalizeMarket(string market)
        => market.Trim().ToUpperInvariant();
}
