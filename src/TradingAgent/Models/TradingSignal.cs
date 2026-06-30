namespace TradingAgent.Models;

public sealed class TradingSignal
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string OriginalSignal { get; set; } = string.Empty;
    public string? ClaudeAction { get; set; }
    public int? Confidence { get; set; }
    public string? RiskLevel { get; set; }
    public decimal? Price { get; set; }
    public string? Timeframe { get; set; }
    public string? Strategy { get; set; }
    public string RawPayload { get; set; } = string.Empty;
    public string? ClaudeRawResponse { get; set; }
    public string? ShortReason { get; set; }
    public decimal? SuggestedStopLoss { get; set; }
    public decimal? SuggestedTakeProfit { get; set; }
    public decimal? RiskRewardRatio { get; set; }
    public decimal? PositionSizePercent { get; set; }
    public bool? ShouldNotify { get; set; }
    public bool Notified { get; set; }
    public bool IsTest { get; set; }
    public string Source { get; set; } = WebhookSources.Unknown;
    public DateTime CreatedAtUtc { get; set; }
    public decimal? ProfitLoss { get; set; }
    public string? Notes { get; set; }
    public bool IsClosed { get; set; }
    public string? IgnoredReason { get; set; }
    public string? IgnoredBy { get; set; }
    public string? ReasonCategories { get; set; }
    public string? MarketStatus { get; set; }
    public string? MarketSession { get; set; }
    public string? MarketName { get; set; }
    public DateTime? MarketCheckedAtUtc { get; set; }
    public SignalMarketData? MarketData { get; set; }
}

public static class SignalIgnoredBy
{
    public const string MarketStatus = "MarketStatus";
    public const string PositionRules = "PositionRules";
}
