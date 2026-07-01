using TradingAgent.DTOs;

namespace TradingAgent.Services;

public static class ConfidenceDecisionBands
{
    public sealed record SessionBands(int BuyThreshold, int WaitMinimum);

    public static SessionBands Get(string marketSession)
        => marketSession switch
        {
            MarketSessionValues.Regular => new SessionBands(60, 40),
            MarketSessionValues.PreMarket => new SessionBands(70, 50),
            MarketSessionValues.AfterHours => new SessionBands(70, 50),
            MarketSessionValues.Overnight => new SessionBands(75, 55),
            _ => new SessionBands(60, 40)
        };

    public static string MapDecision(int confidence, string marketSession)
    {
        var bands = Get(marketSession);
        if (confidence >= bands.BuyThreshold)
        {
            return "BUY";
        }

        if (confidence >= bands.WaitMinimum)
        {
            return "WAIT";
        }

        return "IGNORE";
    }
}
