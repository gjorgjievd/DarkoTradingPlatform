using Microsoft.EntityFrameworkCore;
using TradingAgent.Configuration;
using TradingAgent.Data;
using TradingAgent.DTOs;
using TradingAgent.Models;

namespace TradingAgent.Services;

public sealed class PositionManagerService(
    TradingAgentDbContext dbContext,
    AppSettings settings,
    ILogger<PositionManagerService> logger) : IPositionManagerService
{
    public async Task<PositionActionResult> ProcessWebhookSignalAsync(
        TradingSignal signal,
        MarketContext marketContext,
        ClaudeAnalysisResponse analysisResponse,
        bool notificationPassed,
        CancellationToken cancellationToken)
    {
        if (!settings.PaperTradingEnabled)
        {
            return new PositionActionResult();
        }

        if (analysisResponse.IsFallback || analysisResponse.Analysis is null)
        {
            return new PositionActionResult();
        }

        var decision = analysisResponse.Analysis.Action?.Trim().ToUpperInvariant();
        var currentPrice = ResolveCurrentPrice(signal, marketContext);

        if (decision == "BUY")
        {
            return await TryOpenPositionAsync(signal, analysisResponse, notificationPassed, currentPrice, cancellationToken);
        }

        if (decision is "SELL" or "EXIT")
        {
            return await TryClosePositionAsync(signal, currentPrice, cancellationToken);
        }

        return new PositionActionResult();
    }

    public async Task<Position?> ClosePositionAsync(
        int positionId,
        decimal? exitPrice,
        int? exitSignalId,
        CancellationToken cancellationToken)
    {
        var position = await dbContext.Positions
            .FirstOrDefaultAsync(item => item.Id == positionId, cancellationToken);

        if (position is null || position.Status != PositionStatus.Open)
        {
            return null;
        }

        var resolvedExitPrice = exitPrice ?? position.EntryPrice;
        ApplyClose(position, resolvedExitPrice, exitSignalId);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Position {PositionId} manually closed. Symbol={Symbol}, ProfitLoss={ProfitLoss}",
            position.Id,
            position.Symbol,
            position.ProfitLoss);

        return position;
    }

    private async Task<PositionActionResult> TryOpenPositionAsync(
        TradingSignal signal,
        ClaudeAnalysisResponse analysisResponse,
        bool notificationPassed,
        decimal? currentPrice,
        CancellationToken cancellationToken)
    {
        if (!notificationPassed)
        {
            return new PositionActionResult();
        }

        if (!currentPrice.HasValue || currentPrice.Value <= 0)
        {
            logger.LogWarning("Cannot open paper position for {Symbol}: current price unavailable.", signal.Symbol);
            return new PositionActionResult
            {
                SkippedReason = $"Paper trade skipped: current price unavailable for {signal.Symbol}."
            };
        }

        var hasOpenPosition = await dbContext.Positions.AnyAsync(
            position => position.Symbol == signal.Symbol && position.Status == PositionStatus.Open,
            cancellationToken);

        if (hasOpenPosition)
        {
            var reason = $"Paper trade skipped: OPEN position already exists for {signal.Symbol}.";
            logger.LogInformation(reason);
            return new PositionActionResult { SkippedReason = reason };
        }

        var position = new Position
        {
            Symbol = signal.Symbol,
            Status = PositionStatus.Open,
            EntrySignalId = signal.Id,
            EntryPrice = currentPrice.Value,
            Quantity = settings.DefaultPositionQuantity,
            EntryTimeUtc = DateTime.UtcNow,
            MaxRiskPercent = CalculateMaxRiskPercent(currentPrice.Value, analysisResponse.Analysis?.SuggestedStopLoss)
        };

        dbContext.Positions.Add(position);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Paper position opened. PositionId={PositionId}, Symbol={Symbol}, EntryPrice={EntryPrice}",
            position.Id,
            position.Symbol,
            position.EntryPrice);

        return new PositionActionResult { OpenedPosition = position };
    }

    private async Task<PositionActionResult> TryClosePositionAsync(
        TradingSignal signal,
        decimal? currentPrice,
        CancellationToken cancellationToken)
    {
        var openPosition = await dbContext.Positions
            .FirstOrDefaultAsync(
                position => position.Symbol == signal.Symbol && position.Status == PositionStatus.Open,
                cancellationToken);

        if (openPosition is null)
        {
            logger.LogInformation("No OPEN position to close for symbol {Symbol}.", signal.Symbol);
            return new PositionActionResult();
        }

        if (!currentPrice.HasValue || currentPrice.Value <= 0)
        {
            logger.LogWarning("Cannot close paper position for {Symbol}: current price unavailable.", signal.Symbol);
            return new PositionActionResult
            {
                SkippedReason = $"Paper trade close skipped: current price unavailable for {signal.Symbol}."
            };
        }

        ApplyClose(openPosition, currentPrice.Value, signal.Id);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Paper position closed. PositionId={PositionId}, Symbol={Symbol}, ProfitLoss={ProfitLoss}",
            openPosition.Id,
            openPosition.Symbol,
            openPosition.ProfitLoss);

        return new PositionActionResult { ClosedPosition = openPosition };
    }

    private static void ApplyClose(Position position, decimal exitPrice, int? exitSignalId)
    {
        position.Status = PositionStatus.Closed;
        position.ExitPrice = exitPrice;
        position.ExitSignalId = exitSignalId;
        position.ExitTimeUtc = DateTime.UtcNow;
        position.ProfitLoss = (exitPrice - position.EntryPrice) * position.Quantity;
        position.ProfitLossPercent = position.EntryPrice == 0
            ? null
            : ((exitPrice - position.EntryPrice) / position.EntryPrice) * 100m;
    }

    private static decimal? ResolveCurrentPrice(TradingSignal signal, MarketContext marketContext)
        => marketContext.CurrentPrice ?? signal.Price;

    private static decimal? CalculateMaxRiskPercent(decimal entryPrice, decimal? stopLoss)
    {
        if (!stopLoss.HasValue || entryPrice <= 0)
        {
            return null;
        }

        return Math.Round(Math.Abs(entryPrice - stopLoss.Value) / entryPrice * 100m, 4);
    }
}
