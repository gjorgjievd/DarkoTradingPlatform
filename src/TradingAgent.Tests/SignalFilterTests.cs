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
        MinConfidenceRegular = 65,
        MinConfidencePremarket = 75,
        MinConfidenceAfterHours = 75,
        MinConfidenceOvernight = 85
    };

    private static TradingViewWebhookRequest BuyPayload(
        decimal price = 100m,
        decimal ema9 = 101m,
        decimal ema20 = 99m,
        decimal rsi = 55m,
        decimal volumeSpike = 80m,
        string? strategy = null)
        => new()
        {
            Symbol = "NVDA",
            Signal = "BUY",
            Price = price.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Ema9 = ema9.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Ema20 = ema20.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Rsi = rsi.ToString(System.Globalization.CultureInfo.InvariantCulture),
            VolumeSpike = volumeSpike.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Strategy = strategy
        };

    [Fact]
    public void BuyRegularGoodSignal_DoesNotPreIgnore()
    {
        var payload = BuyPayload(price: 100m);
        var market = new MarketContext { CurrentPrice = 100.5m, Ema9 = 101m, Ema20 = 99m, Rsi14 = 54m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 65 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var result = SignalFilterService.EvaluatePreClaude(context, hasOpenPosition: false);

        Assert.False(result.SkipClaude);
    }

    [Fact]
    public void BuyWithMinorYahooMismatch_BecomesWaitNotIgnore()
    {
        var payload = BuyPayload(price: 100m, rsi: 58m, volumeSpike: 75m);
        var market = new MarketContext { CurrentPrice = 101.2m, Ema9 = 95m, Ema20 = 90m, Rsi14 = 42m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 65 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var analysis = new ClaudeAnalysisResult
        {
            Action = "BUY",
            Confidence = 72,
            ShortReason = "TradingView setup is bullish."
        };

        var adjusted = SignalFilterService.ApplyPostClaudeAdjustments(
            analysis,
            context,
            status,
            CreateSettings(),
            duplicateBuyBlocked: false);

        Assert.NotEqual("IGNORE", adjusted.Action);
        Assert.Contains(SignalReasonCategories.WeakConfirmation, adjusted.ReasonCategories);
        Assert.True(adjusted.Confidence < 72);
    }

    [Fact]
    public void BuyLooseStrategy_ReducesConfidenceButDoesNotAutoIgnore()
    {
        var payload = BuyPayload(strategy: "EMA9_EMA20_LOOSE");
        var market = new MarketContext { CurrentPrice = 100m, Ema9 = 101m, Ema20 = 99m, Rsi14 = 55m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 65 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var analysis = new ClaudeAnalysisResult
        {
            Action = "BUY",
            Confidence = 70,
            ShortReason = "Acceptable loose setup."
        };

        var adjusted = SignalFilterService.ApplyPostClaudeAdjustments(
            analysis,
            context,
            status,
            CreateSettings(),
            duplicateBuyBlocked: false);

        Assert.Equal(60, adjusted.Confidence);
        Assert.NotEqual("IGNORE", adjusted.Action);
    }

    [Fact]
    public void SellWithoutOpenPosition_IsIgnoredBeforeClaude()
    {
        var payload = new TradingViewWebhookRequest { Symbol = "NVDA", Signal = "SELL", Price = "100" };
        var market = new MarketContext { CurrentPrice = 100m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 65 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var result = SignalFilterService.EvaluatePreClaude(context, hasOpenPosition: false);

        Assert.True(result.SkipClaude);
        Assert.Equal("IGNORE", result.Decision);
        Assert.Equal(SignalReasonCategories.NoOpenPositionForSell, result.ReasonCategories);
    }

    [Fact]
    public void AfterHoursThreshold_DefaultIs75()
    {
        var settings = CreateSettings();
        var threshold = MarketSessionConfidence.GetThreshold(settings, MarketSessionValues.AfterHours);
        Assert.Equal(75, threshold);
    }

    [Fact]
    public void ExtremePriceDrift_IsIgnoredBeforeClaude()
    {
        var payload = BuyPayload(price: 110m);
        var market = new MarketContext { CurrentPrice = 100m };
        var status = new MarketStatusDto { MarketSession = MarketSessionValues.Regular, SessionConfidenceThreshold = 65 };
        var context = SignalFilterService.BuildContext(payload, market, status, CreateSettings());

        var result = SignalFilterService.EvaluatePreClaude(context, hasOpenPosition: false);

        Assert.True(result.SkipClaude);
        Assert.Equal("IGNORE", result.Decision);
        Assert.Equal(SignalReasonCategories.PriceDriftWarning, result.ReasonCategories);
    }
}

public class SignalNotificationFilterTests
{
    [Fact]
    public void WaitSignals_AreNotSentByDefault()
    {
        var settings = new AppSettings { SendWaitSignals = false };
        Assert.False(NotificationFilter.ShouldSendTelegramForDecision(settings, "WAIT", 80, 65, true));
    }

    [Fact]
    public void BuySignals_AreSentWhenThresholdMet()
    {
        var settings = new AppSettings();
        Assert.True(NotificationFilter.ShouldSendTelegramForDecision(settings, "BUY", 70, 65, true));
    }
}
