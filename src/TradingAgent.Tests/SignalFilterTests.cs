using TradingAgent.Configuration;
using TradingAgent.DTOs;
using TradingAgent.Models;
using TradingAgent.Services;
using TradingAgent.Services.Market;
using Xunit;

namespace TradingAgent.Tests;

public class SignalFilterServiceTests
{
    private static AppSettings CreateSettings() => new()
    {
        MaxPriceDriftPercentRegular = 1.0m,
        MaxPriceDriftPercentExtended = 2.5m,
        MinConfidenceRegular = 60,
        MinConfidencePremarket = 70,
        MinConfidenceAfterHours = 70,
        MinConfidenceOvernight = 75
    };

    private static TradingViewWebhookRequest BuyPayload(
        decimal price = 100m,
        decimal ema9 = 101m,
        decimal ema20 = 99m,
        decimal ema50 = 98m,
        decimal rsi = 60m,
        decimal volumeSpike = 130m,
        string? strategy = null)
        => new()
        {
            Symbol = "NVDA",
            Signal = "BUY",
            Price = price.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Ema9 = ema9.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Ema20 = ema20.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Ema50 = ema50.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Rsi = rsi.ToString(System.Globalization.CultureInfo.InvariantCulture),
            VolumeSpike = volumeSpike.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Strategy = strategy
        };

    [Fact]
    public void BuyRegularGoodSignal_DoesNotPreIgnore()
    {
        var payload = BuyPayload(price: 100m);
        var market = new MarketContext { CurrentPrice = 100.5m, Ema9 = 80m, Ema20 = 70m, Rsi14 = 30m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 60 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var result = SignalFilterService.EvaluatePreClaude(context, hasOpenPosition: false);

        Assert.False(result.SkipClaude);
        Assert.NotEmpty(context.IndicatorWarnings);
    }

    [Fact]
    public void BuyWithYahooIndicatorMismatch_DoesNotReduceConfidence()
    {
        var payload = BuyPayload(price: 100m);
        var market = new MarketContext { CurrentPrice = 100.2m, Ema9 = 80m, Ema20 = 70m, Rsi14 = 30m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 60 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var analysis = new ClaudeAnalysisResult
        {
            Action = "BUY",
            Confidence = 50,
            ShortReason = "No major external risks detected."
        };

        var adjusted = SignalFilterService.ApplyPostClaudeAdjustments(
            analysis,
            context,
            market,
            status,
            CreateSettings(),
            duplicateBuyBlocked: false);

        Assert.Equal("BUY", adjusted.Action);
        Assert.True(adjusted.Confidence >= 60);
        Assert.DoesNotContain(SignalReasonCategories.WeakConfirmation, adjusted.ReasonCategories ?? string.Empty);
    }

    [Fact]
    public void BuyStrongTradingViewSetup_ProducesBuyDecision()
    {
        var payload = BuyPayload();
        var market = new MarketContext { CurrentPrice = 100m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 60 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var adjusted = SignalFilterService.ApplyPostClaudeAdjustments(
            new ClaudeAnalysisResult { ShortReason = "No significant external risks." },
            context,
            market,
            status,
            CreateSettings(),
            duplicateBuyBlocked: false);

        Assert.Equal("BUY", adjusted.Action);
        Assert.True(adjusted.Confidence >= 60);
    }

    [Fact]
    public void PriceDriftAboveThreePercent_AppliesPenalty()
    {
        var payload = BuyPayload(price: 104m, volumeSpike: 80m, rsi: 50m);
        var market = new MarketContext { CurrentPrice = 100m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 60 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var adjusted = SignalFilterService.ApplyPostClaudeAdjustments(
            new ClaudeAnalysisResult { ShortReason = "Context only." },
            context,
            market,
            status,
            CreateSettings(),
            duplicateBuyBlocked: false);

        Assert.Contains(SignalReasonCategories.PriceDriftWarning, adjusted.ReasonCategories ?? string.Empty);
        Assert.True(adjusted.Confidence <= 85);
    }

    [Fact]
    public void SellWithoutOpenPosition_IsIgnoredBeforeClaude()
    {
        var payload = new TradingViewWebhookRequest { Symbol = "NVDA", Signal = "SELL", Price = "100" };
        var market = new MarketContext { CurrentPrice = 100m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 60 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var result = SignalFilterService.EvaluatePreClaude(context, hasOpenPosition: false);

        Assert.True(result.SkipClaude);
        Assert.Equal("IGNORE", result.Decision);
        Assert.Equal(SignalReasonCategories.NoOpenPositionForSell, result.ReasonCategories);
    }

    [Fact]
    public void AfterHoursBuyThreshold_Is70()
    {
        var settings = CreateSettings();
        var threshold = MarketSessionConfidence.GetThreshold(settings, MarketSessionValues.AfterHours);
        Assert.Equal(70, threshold);
        Assert.Equal(70, ConfidenceDecisionBands.Get(MarketSessionValues.AfterHours).BuyThreshold);
    }

    [Fact]
    public void ExtremePriceDrift_IsIgnoredBeforeClaude()
    {
        var payload = BuyPayload(price: 110m);
        var market = new MarketContext { CurrentPrice = 100m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 60 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var result = SignalFilterService.EvaluatePreClaude(context, hasOpenPosition: false);

        Assert.True(result.SkipClaude);
        Assert.Equal("IGNORE", result.Decision);
    }

    [Fact]
    public void BreakingNegativeNews_ReducesConfidenceSignificantly()
    {
        var payload = BuyPayload(ema9: 101m, ema20: 100m, ema50: 99m, rsi: 50m, volumeSpike: 80m);
        var market = new MarketContext { CurrentPrice = 100m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 60 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var adjusted = SignalFilterService.ApplyPostClaudeAdjustments(
            new ClaudeAnalysisResult
            {
                ShortReason = "Breaking negative news.",
                BreakingNegativeNews = true
            },
            context,
            market,
            status,
            CreateSettings(),
            duplicateBuyBlocked: false);

        Assert.NotEqual("BUY", adjusted.Action);
        Assert.True(adjusted.Confidence < 60);
    }
}

public class ConfidenceDecisionBandTests
{
    [Theory]
    [InlineData(MarketSessionValues.Regular, 60, "BUY")]
    [InlineData(MarketSessionValues.Regular, 45, "WAIT")]
    [InlineData(MarketSessionValues.Regular, 30, "IGNORE")]
    [InlineData(MarketSessionValues.Overnight, 75, "BUY")]
    [InlineData(MarketSessionValues.Overnight, 60, "WAIT")]
    public void MapDecision_UsesSessionBands(string session, int confidence, string expected)
    {
        Assert.Equal(expected, ConfidenceDecisionBands.MapDecision(confidence, session));
    }
}

public class SignalNotificationFilterTests
{
    [Fact]
    public void WaitSignals_AreNotSentByDefault()
    {
        var settings = new AppSettings { SendWaitSignals = false };
        Assert.False(NotificationFilter.ShouldSendTelegramForDecision(settings, "WAIT", 80, 60, true));
    }

    [Fact]
    public void BuySignals_AreSentWhenThresholdMet()
    {
        var settings = new AppSettings();
        Assert.True(NotificationFilter.ShouldSendTelegramForDecision(settings, "BUY", 70, 60, true));
    }
}
