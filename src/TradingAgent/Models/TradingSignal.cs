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
    public DateTime CreatedAtUtc { get; set; }
    public decimal? ProfitLoss { get; set; }
    public string? Notes { get; set; }
    public bool IsClosed { get; set; }
}
