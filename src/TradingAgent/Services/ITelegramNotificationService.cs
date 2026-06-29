using TradingAgent.DTOs;
using TradingAgent.Models;

namespace TradingAgent.Services;

public interface ITelegramNotificationService
{
    Task SendSignalAsync(
        TradingSignal signal,
        bool usedFallback,
        bool isTest,
        MarketStatusDto marketStatus,
        CancellationToken cancellationToken);
    Task SendDuplicateBuyAsync(TradingSignal signal, MarketStatusDto marketStatus, CancellationToken cancellationToken);
    Task SendMarketClosedAsync(TradingSignal signal, MarketStatusDto marketStatus, CancellationToken cancellationToken);
    Task SendPositionOpenedAsync(Position position, CancellationToken cancellationToken);
    Task SendPositionClosedAsync(Position position, CancellationToken cancellationToken);
    Task<TelegramTestResult> SendTestAsync(CancellationToken cancellationToken);
}
