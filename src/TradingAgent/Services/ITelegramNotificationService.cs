using TradingAgent.Models;

namespace TradingAgent.Services;

public interface ITelegramNotificationService
{
    Task SendSignalAsync(TradingSignal signal, bool usedFallback, CancellationToken cancellationToken);
    Task<TelegramTestResult> SendTestAsync(CancellationToken cancellationToken);
}
