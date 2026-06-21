namespace TradingAgent.Services;

public sealed class ClaudeAnalysisResponse
{
    public ClaudeAnalysisResult? Analysis { get; init; }
    public string? RawResponse { get; init; }
    public bool IsFallback { get; init; }
    public string? Error { get; init; }
}

public sealed class ClaudeAnalysisResult
{
    public string? Action { get; set; }
    public int? Confidence { get; set; }
    public string? ShortReason { get; set; }
    public string? RiskLevel { get; set; }
    public decimal? SuggestedStopLoss { get; set; }
    public decimal? SuggestedTakeProfit { get; set; }
}
