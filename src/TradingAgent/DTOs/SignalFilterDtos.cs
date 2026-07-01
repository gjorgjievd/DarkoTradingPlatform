namespace TradingAgent.DTOs;

public sealed class TradingViewIndicators
{
    public string Signal { get; init; } = string.Empty;
    public string? Strategy { get; init; }
    public decimal? Price { get; init; }
    public decimal? Ema9 { get; init; }
    public decimal? Ema20 { get; init; }
    public decimal? Ema50 { get; init; }
    public decimal? Rsi { get; init; }
    public decimal? Volume { get; init; }
    public decimal? AvgVolume { get; init; }
    public decimal? VolumeSpike { get; init; }
    public decimal? Macd { get; init; }
    public string? Timeframe { get; init; }
}

public sealed class SignalFilterContext
{
    public required TradingViewIndicators TradingView { get; init; }
    public decimal? PriceDriftPercent { get; init; }
    public decimal MaxAllowedDriftPercent { get; init; }
    public bool HasPriceDriftWarning { get; init; }
    public bool HasExtremePriceDrift { get; init; }
    public IReadOnlyList<string> IndicatorWarnings { get; init; } = [];
    public bool IsExtendedSession { get; init; }
}

public sealed class PreClaudeFilterResult
{
    public bool SkipClaude { get; init; }
    public string? Decision { get; init; }
    public string? Reason { get; init; }
    public string? ReasonCategories { get; init; }
    public string? IgnoredBy { get; init; }
}
