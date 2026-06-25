using System.Text.Json.Serialization;

namespace TradingAgent.Models;

public sealed class SignalMarketData
{
    public int Id { get; set; }
    public int TradingSignalId { get; set; }

    [JsonIgnore]
    public TradingSignal TradingSignal { get; set; } = null!;
    public string Symbol { get; set; } = string.Empty;
    public decimal? CurrentPrice { get; set; }
    public decimal? Ema9 { get; set; }
    public decimal? Ema20 { get; set; }
    public decimal? Ema50 { get; set; }
    public decimal? Rsi14 { get; set; }
    public decimal? Macd { get; set; }
    public decimal? MacdSignal { get; set; }
    public decimal? Atr { get; set; }
    public long? CurrentVolume { get; set; }
    public long? AverageVolume20 { get; set; }
    public decimal? Week52High { get; set; }
    public decimal? Week52Low { get; set; }
    public DateTime FetchedAtUtc { get; set; }
}
