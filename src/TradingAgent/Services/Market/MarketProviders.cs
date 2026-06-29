using TradingAgent.DTOs;

namespace TradingAgent.Services.Market;

public sealed class NasdaqMarketProvider(IMarketCalendarService calendar) : IMarketProvider
{
    public string MarketName => MarketNames.Nasdaq;

    public MarketStatusDto GetStatus(DateTime utcNow)
        => UsEquityMarketEvaluator.Evaluate(MarketName, "America/New_York", utcNow, calendar);
}

public sealed class NyseMarketProvider(IMarketCalendarService calendar) : IMarketProvider
{
    public string MarketName => MarketNames.Nyse;

    public MarketStatusDto GetStatus(DateTime utcNow)
        => UsEquityMarketEvaluator.Evaluate(MarketName, "America/New_York", utcNow, calendar);
}

public sealed class CryptoMarketProvider : IMarketProvider
{
    public string MarketName => MarketNames.Crypto;

    public MarketStatusDto GetStatus(DateTime utcNow)
        => new()
        {
            MarketName = MarketNames.Crypto,
            TimeZone = "UTC",
            Status = MarketSessionValues.Closed,
            MarketSession = MarketSessionValues.Closed,
            IsOpen = false,
            CurrentMarketTime = utcNow,
            CheckedAtUtc = utcNow,
            Reason = "Crypto market provider is not implemented yet.",
            IsPlaceholder = true
        };
}

public sealed class ForexMarketProvider : IMarketProvider
{
    public string MarketName => MarketNames.Forex;

    public MarketStatusDto GetStatus(DateTime utcNow)
        => new()
        {
            MarketName = MarketNames.Forex,
            TimeZone = "UTC",
            Status = MarketSessionValues.Closed,
            MarketSession = MarketSessionValues.Closed,
            IsOpen = false,
            CurrentMarketTime = utcNow,
            CheckedAtUtc = utcNow,
            Reason = "Forex market provider is not implemented yet.",
            IsPlaceholder = true
        };
}
