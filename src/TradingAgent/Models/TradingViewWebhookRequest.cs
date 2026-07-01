using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingAgent.Models;

public sealed class TradingViewWebhookRequest
{
    [JsonPropertyName("secret")]
    public string? Secret { get; init; }

    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [JsonPropertyName("signal")]
    public string? Signal { get; init; }

    [JsonPropertyName("price")]
    public string? Price { get; init; }

    [JsonPropertyName("timeframe")]
    public string? Timeframe { get; init; }

    [JsonPropertyName("strategy")]
    public string? Strategy { get; init; }

    [JsonPropertyName("rsi")]
    public string? Rsi { get; init; }

    [JsonPropertyName("volume")]
    public string? Volume { get; init; }

    [JsonPropertyName("ema9")]
    public string? Ema9 { get; init; }

    [JsonPropertyName("ema20")]
    public string? Ema20 { get; init; }

    [JsonPropertyName("ema50")]
    public string? Ema50 { get; init; }

    [JsonPropertyName("avgVolume")]
    public string? AvgVolume { get; init; }

    [JsonPropertyName("volumeSpike")]
    public string? VolumeSpike { get; init; }

    [JsonPropertyName("macd")]
    public string? Macd { get; init; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}
