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
        return ShouldSendTelegramForDecision(
            settings,
            analysis.Action,
            analysis.Confidence ?? 0,
            marketStatus.SessionConfidenceThreshold,
            analysis.ShouldNotify);
    }

    public static bool ShouldSendTelegramForDecision(
        AppSettings settings,
        string? action,
        int confidence,
        int threshold,
        bool? claudeShouldNotify)
    {
        if (settings.SendIgnoredSignals)
        {
            return true;
        }

        var normalized = action?.Trim().ToUpperInvariant();
        if (normalized is "IGNORE")
        {
            return false;
        }

        if (normalized == "WAIT")
        {
            return settings.SendWaitSignals && confidence >= threshold;
        }

        if (normalized is "BUY" or "SELL" or "EXIT")
        {
            if (confidence < threshold)
            {
                return false;
            }

            return claudeShouldNotify != false;
        }

        return false;
    }
}
