using TradingAgent.Configuration;

namespace TradingAgent.Services;

internal static class NotificationFilter
{
    public static bool ShouldSendTelegram(AppSettings settings, ClaudeAnalysisResponse response)
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
        return analysis.ShouldNotify == true
            && analysis.Confidence >= settings.MinConfidenceToNotify;
    }
}
