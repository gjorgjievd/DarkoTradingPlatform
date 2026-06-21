using TradingAgent.Models;

namespace TradingAgent.Services;

public interface IClaudeAnalysisService
{
    Task<ClaudeAnalysisResponse> AnalyzeAsync(TradingViewWebhookRequest signal, CancellationToken cancellationToken);
}
