using Microsoft.Extensions.Logging;
using TradingAgent.Configuration;
using TradingAgent.DTOs;
using TradingAgent.Models;

namespace TradingAgent.Services;

public static class ConfidenceEngine
{
    private const int BaseConfidence = 35;
    private const decimal PriceDriftPenaltyThresholdPercent = 3m;
    private const decimal LowLiquidityRatio = 0.20m;

    public static ClaudeAnalysisResult Apply(
        ClaudeAnalysisResult result,
        SignalFilterContext context,
        MarketContext marketContext,
        MarketStatusDto marketStatus,
        AppSettings settings,
        bool duplicateBuyBlocked,
        ILogger? logger = null)
    {
        LogIndicatorMismatches(context, logger);

        var categories = new List<string>();
        var notes = new List<string>();
        var confidence = BaseConfidence;

        if (!string.IsNullOrWhiteSpace(result.ShortReason))
        {
            notes.Add(result.ShortReason);
        }

        if (duplicateBuyBlocked)
        {
            result.Action = "IGNORE";
            result.Confidence = 0;
            result.ShouldNotify = false;
            result.ReasonCategories = SignalReasonCategories.DuplicateBuy;
            result.ShortReason = "Duplicate BUY blocked because an open position already exists.";
            return result;
        }

        confidence += ApplyTradingViewBonuses(context.TradingView, context, marketStatus, notes);
        confidence -= ApplyRiskPenalties(result, context, marketContext, marketStatus, categories, notes);

        confidence = Math.Clamp(confidence, 0, 100);
        result.Confidence = confidence;

        if (SignalFilterService.IsBuySignal(context.TradingView.Signal))
        {
            result.Action = ConfidenceDecisionBands.MapDecision(confidence, marketStatus.MarketSession);
        }
        else if (string.IsNullOrWhiteSpace(result.Action))
        {
            result.Action = confidence >= ConfidenceDecisionBands.Get(marketStatus.MarketSession).BuyThreshold
                ? context.TradingView.Signal
                : "WAIT";
        }

        if (context.HasPriceDriftWarning)
        {
            categories.Add(SignalReasonCategories.PriceDriftWarning);
        }

        if (confidence < ConfidenceDecisionBands.Get(marketStatus.MarketSession).BuyThreshold)
        {
            categories.Add(confidence < ConfidenceDecisionBands.Get(marketStatus.MarketSession).WaitMinimum
                ? SignalReasonCategories.LowConfidence
                : context.IsExtendedSession
                    ? SignalReasonCategories.SessionRisk
                    : SignalReasonCategories.WeakConfirmation);
        }

        if (result.BreakingNegativeNews == true || result.EarningsToday == true || result.HighVolatility == true)
        {
            categories.Add(SignalReasonCategories.SessionRisk);
        }

        result.ShortReason = string.Join(" ", notes.Distinct());
        result.ReasonCategories = SignalReasonCategories.Join(categories);
        result.ShouldNotify = NotificationFilter.ShouldSendTelegramForDecision(
            settings,
            result.Action,
            confidence,
            ConfidenceDecisionBands.Get(marketStatus.MarketSession).BuyThreshold,
            result.ShouldNotify);

        return result;
    }

    private static int ApplyTradingViewBonuses(
        TradingViewIndicators tradingView,
        SignalFilterContext context,
        MarketStatusDto marketStatus,
        List<string> notes)
    {
        var bonus = 0;

        if (tradingView.Ema9 is > 0 && tradingView.Ema20 is > 0 && tradingView.Ema9 > tradingView.Ema20)
        {
            bonus += 15;
            notes.Add("TradingView EMA9 > EMA20.");
        }

        if (tradingView.Ema20 is > 0 && tradingView.Ema50 is > 0 && tradingView.Ema20 > tradingView.Ema50)
        {
            bonus += 10;
            notes.Add("TradingView EMA20 > EMA50.");
        }

        if (tradingView.VolumeSpike is > 120)
        {
            bonus += 10;
            notes.Add("TradingView volume spike above 120%.");
        }

        if (tradingView.Rsi is >= 55 and <= 70)
        {
            bonus += 10;
            notes.Add("TradingView RSI in bullish range (55-70).");
        }

        if (context.PriceDriftPercent is < 1m)
        {
            bonus += 10;
            notes.Add("Price drift below 1%.");
        }

        if (marketStatus.MarketSession == MarketSessionValues.Regular)
        {
            bonus += 5;
            notes.Add("Regular session bonus.");
        }

        return bonus;
    }

    private static int ApplyRiskPenalties(
        ClaudeAnalysisResult result,
        SignalFilterContext context,
        MarketContext marketContext,
        MarketStatusDto marketStatus,
        List<string> categories,
        List<string> notes)
    {
        var penalty = 0;

        if (marketStatus.IsWeekend)
        {
            penalty += 100;
            notes.Add("Weekend session risk.");
        }

        if (marketStatus.IsHoliday)
        {
            penalty += 100;
            notes.Add("Holiday session risk.");
        }

        if (result.TradingHalted == true)
        {
            penalty += 100;
            notes.Add("Trading halt detected.");
        }

        if (context.PriceDriftPercent is > PriceDriftPenaltyThresholdPercent)
        {
            penalty += 20;
            categories.Add(SignalReasonCategories.PriceDriftWarning);
            notes.Add($"Price drift above {PriceDriftPenaltyThresholdPercent:0.#}%.");
        }

        if (result.BreakingNegativeNews == true)
        {
            penalty += 30;
            notes.Add("Breaking negative news.");
        }

        if (result.EarningsToday == true)
        {
            penalty += 20;
            notes.Add("Earnings today.");
        }

        if (result.DangerousGap == true || result.GapPercent is > 5m)
        {
            penalty += 15;
            notes.Add("Dangerous gap detected.");
        }

        if (result.HighVolatility == true)
        {
            penalty += 10;
            notes.Add("High volatility.");
        }

        if (result.LowLiquidity == true || IsLowLiquidity(marketContext))
        {
            penalty += 10;
            notes.Add("Low liquidity.");
        }

        return penalty;
    }

    private static bool IsLowLiquidity(MarketContext marketContext)
    {
        if (!marketContext.CurrentVolume.HasValue || !marketContext.AverageVolume20.HasValue || marketContext.AverageVolume20 <= 0)
        {
            return false;
        }

        return marketContext.CurrentVolume.Value < marketContext.AverageVolume20.Value * LowLiquidityRatio;
    }

    private static void LogIndicatorMismatches(SignalFilterContext context, ILogger? logger)
    {
        if (context.IndicatorWarnings.Count == 0)
        {
            return;
        }

        var message =
            "Indicator mismatch detected. TradingView indicators kept as primary. " +
            "Yahoo used only for contextual validation. " +
            $"Details: {string.Join("; ", context.IndicatorWarnings)}";

        logger?.LogInformation(message);
    }
}
