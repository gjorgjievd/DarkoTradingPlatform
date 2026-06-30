using System.Globalization;
using System.Text.Json;
using TradingAgent.Configuration;
using TradingAgent.DTOs;
using TradingAgent.Models;

namespace TradingAgent.Services;

public static class SignalFilterService
{
    public const decimal ExtremePriceDriftPercent = 5m;

    public static TradingViewIndicators ParseIndicators(TradingViewWebhookRequest payload)
    {
        return new TradingViewIndicators
        {
            Signal = payload.Signal?.Trim().ToUpperInvariant() ?? string.Empty,
            Strategy = payload.Strategy?.Trim(),
            Price = ParseDecimal(payload.Price),
            Ema9 = ParseDecimal(payload.Ema9) ?? ParseFromAdditional(payload, "ema9"),
            Ema20 = ParseDecimal(payload.Ema20) ?? ParseFromAdditional(payload, "ema20"),
            Ema50 = ParseDecimal(payload.Ema50) ?? ParseFromAdditional(payload, "ema50"),
            Rsi = ParseDecimal(payload.Rsi) ?? ParseFromAdditional(payload, "rsi"),
            Volume = ParseDecimal(payload.Volume) ?? ParseFromAdditional(payload, "volume"),
            AvgVolume = ParseDecimal(payload.AvgVolume) ?? ParseFromAdditional(payload, "avgVolume", "avg_volume"),
            VolumeSpike = ParseDecimal(payload.VolumeSpike) ?? ParseFromAdditional(payload, "volumeSpike", "volume_spike")
        };
    }

    public static SignalFilterContext BuildContext(
        TradingViewWebhookRequest payload,
        MarketContext marketContext,
        MarketStatusDto marketStatus,
        AppSettings settings)
    {
        var tradingView = ParseIndicators(payload);
        var isExtendedSession = marketStatus.MarketSession is MarketSessionValues.PreMarket
            or MarketSessionValues.AfterHours
            or MarketSessionValues.Overnight;

        var maxAllowedDrift = isExtendedSession
            ? settings.MaxPriceDriftPercentExtended
            : settings.MaxPriceDriftPercentRegular;

        var priceDriftPercent = CalculatePriceDriftPercent(tradingView.Price, marketContext.CurrentPrice);
        var hasExtremePriceDrift = priceDriftPercent.HasValue && priceDriftPercent.Value > ExtremePriceDriftPercent;
        var hasPriceDriftWarning = priceDriftPercent.HasValue && priceDriftPercent.Value > maxAllowedDrift;
        var priceDriftPenalty = CalculatePriceDriftPenalty(priceDriftPercent, maxAllowedDrift, hasExtremePriceDrift);

        var indicatorWarnings = BuildIndicatorWarnings(tradingView, marketContext);
        var indicatorPenalty = Math.Clamp(indicatorWarnings.Count * 5, 0, 15);

        var isLooseStrategy = tradingView.Strategy?.Contains("LOOSE", StringComparison.OrdinalIgnoreCase) == true;

        return new SignalFilterContext
        {
            TradingView = tradingView,
            PriceDriftPercent = priceDriftPercent,
            MaxAllowedDriftPercent = maxAllowedDrift,
            HasPriceDriftWarning = hasPriceDriftWarning,
            HasExtremePriceDrift = hasExtremePriceDrift,
            PriceDriftConfidencePenalty = priceDriftPenalty,
            IndicatorMismatchPenalty = indicatorPenalty,
            IndicatorWarnings = indicatorWarnings,
            IsLooseStrategy = isLooseStrategy,
            LooseStrategyPenalty = isLooseStrategy ? 10 : 0,
            IsExtendedSession = isExtendedSession
        };
    }

    public static PreClaudeFilterResult EvaluatePreClaude(
        SignalFilterContext context,
        bool hasOpenPosition)
    {
        var signal = context.TradingView.Signal;
        if (signal is "SELL" or "EXIT" && !hasOpenPosition)
        {
            return new PreClaudeFilterResult
            {
                SkipClaude = true,
                Decision = "IGNORE",
                Reason = "SELL signal received but no open position exists",
                ReasonCategories = SignalReasonCategories.NoOpenPositionForSell,
                IgnoredBy = SignalIgnoredBy.PositionRules
            };
        }

        if (context.HasExtremePriceDrift)
        {
            return new PreClaudeFilterResult
            {
                SkipClaude = true,
                Decision = "IGNORE",
                Reason =
                    $"Extreme price drift detected ({context.PriceDriftPercent:0.##}% between TradingView and Yahoo).",
                ReasonCategories = SignalReasonCategories.PriceDriftWarning,
                IgnoredBy = SignalIgnoredBy.PositionRules
            };
        }

        return new PreClaudeFilterResult();
    }

    public static ClaudeAnalysisResult ApplyPostClaudeAdjustments(
        ClaudeAnalysisResult result,
        SignalFilterContext context,
        MarketStatusDto marketStatus,
        AppSettings settings,
        bool duplicateBuyBlocked)
    {
        var categories = new List<string>();
        var tradingView = context.TradingView;
        var threshold = marketStatus.SessionConfidenceThreshold;
        var confidence = result.Confidence ?? 0;
        var reasonParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(result.ShortReason))
        {
            reasonParts.Add(result.ShortReason);
        }

        if (duplicateBuyBlocked)
        {
            result.Action = "IGNORE";
            result.ShouldNotify = false;
            categories.Add(SignalReasonCategories.DuplicateBuy);
            reasonParts.Add("Duplicate BUY blocked because an open position already exists.");
            result.ShortReason = string.Join(" ", reasonParts);
            result.ReasonCategories = SignalReasonCategories.Join(categories);
            return result;
        }

        if (context.HasPriceDriftWarning)
        {
            categories.Add(SignalReasonCategories.PriceDriftWarning);
            reasonParts.Add(
                $"Price drift warning: TradingView/Yahoo differ by {context.PriceDriftPercent:0.##}% (allowed {context.MaxAllowedDriftPercent:0.##}%).");
        }

        if (context.IndicatorWarnings.Count > 0)
        {
            categories.Add(SignalReasonCategories.WeakConfirmation);
            reasonParts.Add($"Indicator mismatch warning (TradingView is primary): {string.Join("; ", context.IndicatorWarnings)}.");
        }

        if (context.IsLooseStrategy)
        {
            categories.Add(SignalReasonCategories.WeakConfirmation);
            reasonParts.Add("LOOSE strategy reduces confidence.");
        }

        confidence -= context.PriceDriftConfidencePenalty
            + context.IndicatorMismatchPenalty
            + context.LooseStrategyPenalty;
        confidence = Math.Clamp(confidence, 0, 100);
        result.Confidence = confidence;

        if (string.Equals(tradingView.Signal, "BUY", StringComparison.OrdinalIgnoreCase))
        {
            if (tradingView.Rsi is < 45)
            {
                result.Action = "IGNORE";
                categories.Add(SignalReasonCategories.WeakConfirmation);
                reasonParts.Add("TradingView RSI is below 45 for a BUY signal.");
            }
            else if (tradingView.VolumeSpike is < 50)
            {
                result.Action = "IGNORE";
                categories.Add(SignalReasonCategories.WeakConfirmation);
                reasonParts.Add("TradingView volumeSpike is below 50 for a BUY signal.");
            }
            else if (confidence < threshold)
            {
                if (confidence >= 40)
                {
                    if (!string.Equals(result.Action, "IGNORE", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Action = "WAIT";
                    }

                    categories.Add(context.IsExtendedSession
                        ? SignalReasonCategories.SessionRisk
                        : SignalReasonCategories.WeakConfirmation);
                    reasonParts.Add($"Confidence {confidence} is below session threshold {threshold}.");
                }
                else
                {
                    result.Action = "IGNORE";
                    categories.Add(SignalReasonCategories.LowConfidence);
                    reasonParts.Add($"Confidence {confidence} is too low for this session.");
                }
            }
        }

        result.ShortReason = string.Join(" ", reasonParts.Distinct());
        result.ShouldNotify = NotificationFilter.ShouldSendTelegramForDecision(
            settings,
            result.Action,
            confidence,
            threshold,
            result.ShouldNotify);

        result.ReasonCategories = SignalReasonCategories.Join(categories);
        return result;
    }

    public static bool IsBuySignal(string? signal)
        => string.Equals(signal, "BUY", StringComparison.OrdinalIgnoreCase);

    private static decimal? CalculatePriceDriftPercent(decimal? tradingViewPrice, decimal? yahooPrice)
    {
        if (!tradingViewPrice.HasValue || !yahooPrice.HasValue || yahooPrice.Value == 0)
        {
            return null;
        }

        return Math.Abs(tradingViewPrice.Value - yahooPrice.Value) / yahooPrice.Value * 100m;
    }

    private static int CalculatePriceDriftPenalty(decimal? driftPercent, decimal maxAllowedDrift, bool hasExtremePriceDrift)
    {
        if (!driftPercent.HasValue || hasExtremePriceDrift || driftPercent.Value <= maxAllowedDrift)
        {
            return 0;
        }

        var excess = driftPercent.Value - maxAllowedDrift;
        return Math.Clamp((int)Math.Ceiling(excess * 5m), 5, 20);
    }

    private static List<string> BuildIndicatorWarnings(TradingViewIndicators tradingView, MarketContext marketContext)
    {
        var warnings = new List<string>();
        AddMismatchWarning(warnings, "EMA9", tradingView.Ema9, marketContext.Ema9, relativeTolerancePercent: 3m);
        AddMismatchWarning(warnings, "EMA20", tradingView.Ema20, marketContext.Ema20, relativeTolerancePercent: 3m);
        AddMismatchWarning(warnings, "EMA50", tradingView.Ema50, marketContext.Ema50, relativeTolerancePercent: 3m);
        AddMismatchWarning(warnings, "RSI", tradingView.Rsi, marketContext.Rsi14, absoluteTolerance: 8m);
        return warnings;
    }

    private static void AddMismatchWarning(
        List<string> warnings,
        string name,
        decimal? tradingViewValue,
        decimal? yahooValue,
        decimal relativeTolerancePercent = 0m,
        decimal absoluteTolerance = 0m)
    {
        if (!tradingViewValue.HasValue || !yahooValue.HasValue)
        {
            return;
        }

        if (relativeTolerancePercent > 0)
        {
            var baseline = Math.Max(Math.Abs(yahooValue.Value), 0.01m);
            var relativeDiff = Math.Abs(tradingViewValue.Value - yahooValue.Value) / baseline * 100m;
            if (relativeDiff > relativeTolerancePercent)
            {
                warnings.Add($"{name} TV={tradingViewValue:0.##} vs Yahoo={yahooValue:0.##}");
            }

            return;
        }

        if (Math.Abs(tradingViewValue.Value - yahooValue.Value) > absoluteTolerance)
        {
            warnings.Add($"{name} TV={tradingViewValue:0.##} vs Yahoo={yahooValue:0.##}");
        }
    }

    private static decimal? ParseFromAdditional(TradingViewWebhookRequest payload, params string[] keys)
    {
        if (payload.AdditionalData is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (payload.AdditionalData.TryGetValue(key, out var element))
            {
                return ParseJsonElement(element);
            }
        }

        return null;
    }

    private static decimal? ParseJsonElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.String => ParseDecimal(element.GetString()),
            _ => null
        };

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
