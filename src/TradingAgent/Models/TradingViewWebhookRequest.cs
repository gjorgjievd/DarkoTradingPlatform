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

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}
