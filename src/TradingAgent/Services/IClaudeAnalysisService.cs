using TradingAgent.DTOs;
using TradingAgent.Models;

namespace TradingAgent.Services;

public interface IClaudeAnalysisService
{
    Task<ClaudeAnalysisResponse> AnalyzeAsync(
        TradingViewWebhookRequest signal,
        MarketContext? marketContext,
        MarketStatusDto marketStatus,
        SignalFilterContext filterContext,
        CancellationToken cancellationToken);

    Task<ClaudeTestResult> TestAsync(CancellationToken cancellationToken);
}
