namespace TradingAgent.Services;

internal static class TechnicalIndicators
{
    public static decimal? CalculateEma(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count < period)
        {
            return null;
        }

        var multiplier = 2m / (period + 1);
        var ema = values.Take(period).Average();

        for (var i = period; i < values.Count; i++)
        {
            ema = (values[i] - ema) * multiplier + ema;
        }

        return Math.Round(ema, 4);
    }

    public static decimal? CalculateRsi(IReadOnlyList<decimal> closes, int period = 14)
    {
        if (closes.Count <= period)
        {
            return null;
        }

        decimal gains = 0;
        decimal losses = 0;

        for (var i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change >= 0)
            {
                gains += change;
            }
            else
            {
                losses += Math.Abs(change);
            }
        }

        var averageGain = gains / period;
        var averageLoss = losses / period;

        for (var i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            var gain = change > 0 ? change : 0;
            var loss = change < 0 ? Math.Abs(change) : 0;

            averageGain = ((averageGain * (period - 1)) + gain) / period;
            averageLoss = ((averageLoss * (period - 1)) + loss) / period;
        }

        if (averageLoss == 0)
        {
            return 100;
        }

        var rs = averageGain / averageLoss;
        return Math.Round(100 - (100 / (1 + rs)), 2);
    }

    public static (decimal? Macd, decimal? Signal) CalculateMacd(
        IReadOnlyList<decimal> closes,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9)
    {
        if (closes.Count < slowPeriod + signalPeriod)
        {
            return (null, null);
        }

        var macdSeries = new List<decimal>();
        for (var i = slowPeriod - 1; i < closes.Count; i++)
        {
            var slice = closes.Take(i + 1).ToList();
            var fastEma = CalculateEma(slice, fastPeriod);
            var slowEma = CalculateEma(slice, slowPeriod);
            if (fastEma is null || slowEma is null)
            {
                continue;
            }

            macdSeries.Add(fastEma.Value - slowEma.Value);
        }

        if (macdSeries.Count < signalPeriod)
        {
            return (null, null);
        }

        var macd = Math.Round(macdSeries[^1], 4);
        var signal = CalculateEma(macdSeries, signalPeriod);
        return (macd, signal);
    }

    public static decimal? CalculateAtr(
        IReadOnlyList<decimal> highs,
        IReadOnlyList<decimal> lows,
        IReadOnlyList<decimal> closes,
        int period = 14)
    {
        if (highs.Count != lows.Count || highs.Count != closes.Count || closes.Count <= period)
        {
            return null;
        }

        var trueRanges = new List<decimal>();
        for (var i = 1; i < closes.Count; i++)
        {
            var highLow = highs[i] - lows[i];
            var highClose = Math.Abs(highs[i] - closes[i - 1]);
            var lowClose = Math.Abs(lows[i] - closes[i - 1]);
            trueRanges.Add(Math.Max(highLow, Math.Max(highClose, lowClose)));
        }

        if (trueRanges.Count < period)
        {
            return null;
        }

        var atr = trueRanges.Take(period).Average();
        for (var i = period; i < trueRanges.Count; i++)
        {
            atr = ((atr * (period - 1)) + trueRanges[i]) / period;
        }

        return Math.Round(atr, 4);
    }

    public static long? CalculateAverageVolume(IReadOnlyList<long> volumes, int period = 20)
    {
        if (volumes.Count < period)
        {
            return null;
        }

        return (long)Math.Round(volumes.TakeLast(period).Average());
    }
}
