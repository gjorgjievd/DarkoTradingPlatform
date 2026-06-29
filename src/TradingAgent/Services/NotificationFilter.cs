using TradingAgent.Configuration;
using TradingAgent.DTOs;

namespace TradingAgent.Services;

internal static class NotificationFilter
{
    public static bool ShouldSendTelegram(
        AppSettings settings,
        ClaudeAnalysisResponse response,
        MarketStatusDto marketStatus)
    {
        if (settings.SendIgnoredSignals)
        {
            return true;
        }

        if (response.IsFallback || response.Analysis is null)
        {
            return false;
        }

        var analysis = response.Analysis;
        var threshold = marketStatus.SessionConfidenceThreshold;
        return analysis.ShouldNotify == true
            && analysis.Confidence >= threshold;
    }
}
