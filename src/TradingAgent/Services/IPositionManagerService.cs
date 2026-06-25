using TradingAgent.DTOs;
using TradingAgent.Models;

namespace TradingAgent.Services;

public sealed class PositionActionResult
{
    public Position? OpenedPosition { get; init; }
    public Position? ClosedPosition { get; init; }
    public string? SkippedReason { get; init; }
}

public interface IPositionManagerService
{
    Task<PositionActionResult> ProcessWebhookSignalAsync(
        TradingSignal signal,
        MarketContext marketContext,
        ClaudeAnalysisResponse analysisResponse,
        bool notificationPassed,
        CancellationToken cancellationToken);

    Task<Position?> ClosePositionAsync(
        int positionId,
        decimal? exitPrice,
        int? exitSignalId,
        CancellationToken cancellationToken);
}
