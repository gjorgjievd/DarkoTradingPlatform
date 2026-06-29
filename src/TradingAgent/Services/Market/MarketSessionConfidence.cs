using TradingAgent.Configuration;
using TradingAgent.DTOs;

namespace TradingAgent.Services.Market;

internal static class MarketSessionConfidence
{
    public static int GetThreshold(AppSettings settings, string marketSession)
        => marketSession switch
        {
            MarketSessionValues.Regular => settings.MinConfidenceRegular,
            MarketSessionValues.PreMarket => settings.MinConfidencePremarket,
            MarketSessionValues.AfterHours => settings.MinConfidenceAfterHours,
            MarketSessionValues.Overnight => settings.MinConfidenceOvernight,
            _ => settings.MinConfidenceToNotify
        };
}
