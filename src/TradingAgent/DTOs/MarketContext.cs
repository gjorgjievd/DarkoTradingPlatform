namespace TradingAgent.DTOs;

public sealed class MarketContext
{
    public string Symbol { get; init; } = string.Empty;
    public decimal? CurrentPrice { get; init; }
    public decimal? Ema9 { get; init; }
    public decimal? Ema20 { get; init; }
    public decimal? Ema50 { get; init; }
    public decimal? Rsi14 { get; init; }
    public decimal? Macd { get; init; }
    public decimal? MacdSignal { get; init; }
    public decimal? Atr { get; init; }
    public long? CurrentVolume { get; init; }
    public long? AverageVolume20 { get; init; }
    public decimal? Week52High { get; init; }
    public decimal? Week52Low { get; init; }
    public DateTime FetchedAtUtc { get; init; }
    public string? Error { get; init; }
}
