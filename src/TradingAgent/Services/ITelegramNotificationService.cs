using TradingAgent.Models;

namespace TradingAgent.Services;

public interface ITelegramNotificationService
{
    Task SendSignalAsync(TradingSignal signal, bool usedFallback, bool isTest, CancellationToken cancellationToken);
    Task SendPositionOpenedAsync(Position position, CancellationToken cancellationToken);
    Task SendPositionClosedAsync(Position position, CancellationToken cancellationToken);
    Task<TelegramTestResult> SendTestAsync(CancellationToken cancellationToken);
}
