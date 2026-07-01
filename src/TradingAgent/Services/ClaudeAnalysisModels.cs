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
    public decimal? RiskRewardRatio { get; set; }
    public decimal? PositionSizePercent { get; set; }
    public bool? ShouldNotify { get; set; }
    public string? ReasonCategories { get; set; }
    public bool? BreakingNegativeNews { get; set; }
    public bool? EarningsToday { get; set; }
    public bool? HighVolatility { get; set; }
    public bool? TradingHalted { get; set; }
    public bool? DangerousGap { get; set; }
    public decimal? GapPercent { get; set; }
    public bool? LowLiquidity { get; set; }
    public string? NewsSummary { get; set; }
}

public sealed class ClaudeTestResult
{
    public bool Success { get; init; }
    public int HttpStatusCode { get; init; }
    public string Model { get; init; } = string.Empty;
    public string? RawResponse { get; init; }
    public ClaudeAnalysisResult? ParsedResponse { get; init; }
    public double ElapsedMilliseconds { get; init; }
    public string? Error { get; init; }
}

public sealed class TelegramTestResult
{
    public bool Success { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? Error { get; init; }
}
