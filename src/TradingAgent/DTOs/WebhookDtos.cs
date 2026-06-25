namespace TradingAgent.DTOs;

public sealed class TestWebhookRequest
{
    public string? Symbol { get; init; }
    public string? Signal { get; init; }
    public string? Price { get; init; }
    public string? Timeframe { get; init; }
    public string? Strategy { get; init; }
}

public sealed class WebhookProcessResponse
{
    public bool Success { get; init; }
    public int? WebhookLogId { get; init; }
    public int? SignalId { get; init; }
    public string? Source { get; init; }
    public bool IsTest { get; init; }
    public string? ResultStatus { get; init; }
    public string? Error { get; init; }
    public object? Signal { get; init; }
}
