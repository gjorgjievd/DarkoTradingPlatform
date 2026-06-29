namespace TradingAgent.DTOs;

public sealed class MarketStatusDto
{
    public string MarketName { get; init; } = string.Empty;
    public string TimeZone { get; init; } = "America/New_York";
    public string Status { get; init; } = MarketSessionValues.Closed;
    public string MarketSession { get; init; } = MarketSessionValues.Closed;
    public int SessionConfidenceThreshold { get; init; }
    public bool IsOpen { get; init; }
    public bool IsWeekend { get; init; }
    public bool IsHoliday { get; init; }
    public bool IsPreMarket { get; init; }
    public bool IsAfterHours { get; init; }
    public bool IsOvernight { get; init; }
    public DateTime CurrentMarketTime { get; init; }
    public DateTime CheckedAtUtc { get; init; }
    public DateTime? NextOpenTimeUtc { get; init; }
    public DateTime? NextCloseTimeUtc { get; init; }
    public string? Reason { get; init; }
    public bool IsPlaceholder { get; init; }
}

public sealed class MarketHolidayDto
{
    public string Name { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public DateOnly ObservedDate { get; init; }
    public bool IsObserved { get; init; }
}

public static class MarketSessionValues
{
    public const string Regular = "REGULAR";
    public const string PreMarket = "PRE_MARKET";
    public const string AfterHours = "AFTER_HOURS";
    public const string Overnight = "OVERNIGHT";
    public const string Closed = "CLOSED";
    public const string Holiday = "HOLIDAY";
    public const string Weekend = "WEEKEND";
}

/// <summary>Backward-compatible aliases for <see cref="MarketSessionValues"/>.</summary>
public static class MarketStatusValues
{
    public const string Open = MarketSessionValues.Regular;
    public const string Closed = MarketSessionValues.Closed;
    public const string PreMarket = MarketSessionValues.PreMarket;
    public const string AfterHours = MarketSessionValues.AfterHours;
    public const string Overnight = MarketSessionValues.Overnight;
    public const string Holiday = MarketSessionValues.Holiday;
    public const string Weekend = MarketSessionValues.Weekend;
}

public static class MarketNames
{
    public const string Nasdaq = "NASDAQ";
    public const string Nyse = "NYSE";
    public const string Crypto = "CRYPTO";
    public const string Forex = "FOREX";
}
