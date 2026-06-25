using TradingAgent.DTOs;
using TradingAgent.Models;

namespace TradingAgent.Services;

internal static class MarketContextMapper
{
    public static SignalMarketData ToEntity(MarketContext context, int tradingSignalId)
        => new()
        {
            TradingSignalId = tradingSignalId,
            Symbol = context.Symbol,
            CurrentPrice = context.CurrentPrice,
            Ema9 = context.Ema9,
            Ema20 = context.Ema20,
            Ema50 = context.Ema50,
            Rsi14 = context.Rsi14,
            Macd = context.Macd,
            MacdSignal = context.MacdSignal,
            Atr = context.Atr,
            CurrentVolume = context.CurrentVolume,
            AverageVolume20 = context.AverageVolume20,
            Week52High = context.Week52High,
            Week52Low = context.Week52Low,
            FetchedAtUtc = context.FetchedAtUtc
        };

    public static MarketContext FromEntity(SignalMarketData entity)
        => new()
        {
            Symbol = entity.Symbol,
            CurrentPrice = entity.CurrentPrice,
            Ema9 = entity.Ema9,
            Ema20 = entity.Ema20,
            Ema50 = entity.Ema50,
            Rsi14 = entity.Rsi14,
            Macd = entity.Macd,
            MacdSignal = entity.MacdSignal,
            Atr = entity.Atr,
            CurrentVolume = entity.CurrentVolume,
            AverageVolume20 = entity.AverageVolume20,
            Week52High = entity.Week52High,
            Week52Low = entity.Week52Low,
            FetchedAtUtc = entity.FetchedAtUtc
        };
}
