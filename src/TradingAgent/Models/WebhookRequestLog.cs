namespace TradingAgent.Models;

public static class WebhookSources
{
    public const string TradingView = "TRADINGVIEW";
    public const string CursorTest = "CURSOR_TEST";
    public const string PostmanTest = "POSTMAN_TEST";
    public const string Unknown = "UNKNOWN";
}

public sealed class WebhookRequestLog
{
    public int Id { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public string Source { get; set; } = WebhookSources.Unknown;
    public string? RemoteIp { get; set; }
    public string? UserAgent { get; set; }
    public string RawPayload { get; set; } = string.Empty;
    public string? HeadersJson { get; set; }
    public bool IsTest { get; set; }
    public int? TradingSignalId { get; set; }
    public string ResultStatus { get; set; } = "PENDING";
    public string? ErrorMessage { get; set; }
}
