using TradingAgent.DTOs;
using TradingAgent.Models;

namespace TradingAgent.Services;

public interface IClaudeAnalysisService
{
    Task<ClaudeAnalysisResponse> AnalyzeAsync(
        TradingViewWebhookRequest signal,
        MarketContext? marketContext,
        CancellationToken cancellationToken);

    Task<ClaudeTestResult> TestAsync(CancellationToken cancellationToken);
}
