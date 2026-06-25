using TradingAgent.DTOs;

namespace TradingAgent.Services;

public interface IYahooFinanceService
{
    Task<MarketContext> FetchMarketContextAsync(string symbol, CancellationToken cancellationToken);
}
