using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingAgent.Configuration;
using TradingAgent.Data;
using TradingAgent.DTOs;
using TradingAgent.Models;
using TradingAgent.Services.Market;

namespace TradingAgent.Services;

public interface IWebhookProcessorService
{
    Task<WebhookProcessResponse> ProcessAsync(
        HttpRequest request,
        string rawPayload,
        bool forceTest);
}

public sealed class WebhookProcessorService(
    TradingAgentDbContext dbContext,
    IClaudeAnalysisService claudeAnalysisService,
    ITelegramNotificationService telegramNotificationService,
    IYahooFinanceService yahooFinanceService,
    IPositionManagerService positionManagerService,
    IMarketStatusService marketStatusService,
    AppSettings settings,
    ILogger<WebhookProcessorService> logger) : IWebhookProcessorService
{
    private static readonly CancellationToken DbCancellation = CancellationToken.None;

    public async Task<WebhookProcessResponse> ProcessAsync(
        HttpRequest request,
        string rawPayload,
        bool forceTest)
    {
        var webhookLog = new WebhookRequestLog
        {
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload = rawPayload ?? string.Empty,
            RemoteIp = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = request.Headers.UserAgent.ToString(),
            HeadersJson = SerializeHeaders(request.Headers)
        };

        dbContext.WebhookRequestLogs.Add(webhookLog);
        await SaveChangesAsync();

        logger.LogInformation("Webhook received. WebhookLogId={WebhookLogId}", webhookLog.Id);

        using var processingCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Max(settings.ClaudeTimeoutSeconds + 30, 60)));
        var workToken = processingCts.Token;

        try
        {
            if (string.IsNullOrWhiteSpace(rawPayload))
            {
                return await FailAsync(webhookLog, WebhookResultStatuses.BadRequest, "Request body is required.");
            }

            TradingViewWebhookRequest? payload;
            try
            {
                payload = JsonSerializer.Deserialize<TradingViewWebhookRequest>(
                    rawPayload,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (JsonException)
            {
                return await FailAsync(webhookLog, WebhookResultStatuses.BadRequest, "Invalid JSON payload.");
            }

            var (source, isTest) = forceTest
                ? (WebhookSources.CursorTest, true)
                : WebhookSourceDetector.Detect(request, rawPayload, payload);

            webhookLog.Source = source;
            webhookLog.IsTest = isTest;
            await SaveChangesAsync();

            if (payload is null || string.IsNullOrWhiteSpace(payload.Symbol) || string.IsNullOrWhiteSpace(payload.Signal))
            {
                return await FailAsync(webhookLog, WebhookResultStatuses.BadRequest, "Payload must include symbol and signal.");
            }

            var headerSecret = request.Headers["X-Webhook-Secret"].FirstOrDefault()
                ?? request.Headers["X-TradingView-Secret"].FirstOrDefault();

            var providedSecret = string.IsNullOrWhiteSpace(payload.Secret) ? headerSecret : payload.Secret;
            if (!string.Equals(providedSecret, settings.WebhookSecret, StringComparison.Ordinal))
            {
                logger.LogWarning("Rejected webhook for symbol {Symbol} because of an invalid secret.", payload.Symbol);
                return await FailAsync(webhookLog, WebhookResultStatuses.Unauthorized, "Invalid webhook secret.");
            }

            logger.LogInformation(
                "Webhook validated. Source={Source}, IsTest={IsTest}, Symbol={Symbol}, Signal={Signal}",
                source,
                isTest,
                payload.Symbol.Trim().ToUpperInvariant(),
                payload.Signal.Trim().ToUpperInvariant());

            var tradingSignal = new TradingSignal
            {
                Symbol = payload.Symbol.Trim().ToUpperInvariant(),
                OriginalSignal = payload.Signal.Trim().ToUpperInvariant(),
                Price = ParseDecimal(payload.Price),
                Timeframe = payload.Timeframe?.Trim(),
                Strategy = payload.Strategy?.Trim(),
                RawPayload = rawPayload,
                IsTest = isTest,
                Source = source,
                CreatedAtUtc = DateTime.UtcNow
            };

            dbContext.TradingSignals.Add(tradingSignal);
            await SaveChangesAsync();

            webhookLog.TradingSignalId = tradingSignal.Id;
            await SaveChangesAsync();

            logger.LogInformation("Signal saved. SignalId={SignalId}, Symbol={Symbol}", tradingSignal.Id, tradingSignal.Symbol);

            var marketStatus = marketStatusService.GetStatus();
            tradingSignal.MarketName = marketStatus.MarketName;
            tradingSignal.MarketStatus = marketStatus.Status;
            tradingSignal.MarketSession = marketStatus.MarketSession;
            tradingSignal.MarketCheckedAtUtc = marketStatus.CheckedAtUtc;

            if (marketStatusService.ShouldIgnoreSignal(marketStatus))
            {
                return await FinalizeMarketIgnoredAsync(webhookLog, tradingSignal, marketStatus, isTest, source);
            }

            MarketContext marketContext;
            try
            {
                logger.LogInformation("Yahoo fetch started. Symbol={Symbol}", tradingSignal.Symbol);
                marketContext = await yahooFinanceService.FetchMarketContextAsync(tradingSignal.Symbol, workToken);
                logger.LogInformation("Yahoo fetch completed. Symbol={Symbol}", tradingSignal.Symbol);
            }
            catch (Exception exception) when (exception is OperationCanceledException or TaskCanceledException)
            {
                logger.LogWarning(exception, "Yahoo fetch canceled or timed out. Symbol={Symbol}", tradingSignal.Symbol);
                return await FinalizeAiFailureAsync(
                    webhookLog,
                    tradingSignal,
                    source,
                    isTest,
                    "Yahoo Finance fetch timeout/failure");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Yahoo fetch failed. Symbol={Symbol}", tradingSignal.Symbol);
                return await FinalizeAiFailureAsync(
                    webhookLog,
                    tradingSignal,
                    source,
                    isTest,
                    "Yahoo Finance fetch failed");
            }

            var marketDataEntity = MarketContextMapper.ToEntity(marketContext, tradingSignal.Id);
            dbContext.SignalMarketData.Add(marketDataEntity);
            await SaveChangesAsync();
            tradingSignal.MarketData = marketDataEntity;

            var filterContext = SignalFilterService.BuildContext(payload, marketContext, marketStatus, settings);
            var hasOpenPosition = await dbContext.Positions.AnyAsync(
                position => position.Symbol == tradingSignal.Symbol && position.Status == PositionStatus.Open,
                DbCancellation);

            var preClaudeResult = SignalFilterService.EvaluatePreClaude(filterContext, hasOpenPosition);
            if (preClaudeResult.SkipClaude)
            {
                return await FinalizePreClaudeAsync(
                    webhookLog,
                    tradingSignal,
                    preClaudeResult,
                    source,
                    isTest);
            }

            ClaudeAnalysisResponse analysisResponse;
            try
            {
                logger.LogInformation("Claude analysis started. SignalId={SignalId}", tradingSignal.Id);
                analysisResponse = await claudeAnalysisService.AnalyzeAsync(
                    payload,
                    marketContext,
                    marketStatus,
                    filterContext,
                    workToken);
                logger.LogInformation(
                    "Claude analysis completed. SignalId={SignalId}, IsFallback={IsFallback}",
                    tradingSignal.Id,
                    analysisResponse.IsFallback);
            }
            catch (Exception exception) when (exception is OperationCanceledException or TaskCanceledException)
            {
                logger.LogWarning(exception, "Claude analysis canceled or timed out. SignalId={SignalId}", tradingSignal.Id);
                return await FinalizeAiFailureAsync(
                    webhookLog,
                    tradingSignal,
                    source,
                    isTest,
                    "Claude analysis timeout/failure");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Claude analysis failed unexpectedly. SignalId={SignalId}", tradingSignal.Id);
                return await FinalizeAiFailureAsync(
                    webhookLog,
                    tradingSignal,
                    source,
                    isTest,
                    "Claude analysis timeout/failure");
            }

            if (analysisResponse.IsFallback)
            {
                return await FinalizeAiFailureAsync(
                    webhookLog,
                    tradingSignal,
                    source,
                    isTest,
                    analysisResponse.Error ?? "Claude analysis timeout/failure",
                    analysisResponse.RawResponse);
            }

            if (analysisResponse.Analysis is not null)
            {
                SignalFilterService.ApplyPostClaudeAdjustments(
                    analysisResponse.Analysis,
                    filterContext,
                    marketContext,
                    marketStatus,
                    settings,
                    duplicateBuyBlocked: false,
                    logger);
            }

            ApplyClaudeAnalysis(tradingSignal, analysisResponse);
            await SaveChangesAsync();

            var duplicateBuyBlocked = await TryBlockDuplicateBuyAsync(tradingSignal, analysisResponse);
            if (duplicateBuyBlocked && analysisResponse.Analysis is not null)
            {
                SignalFilterService.ApplyPostClaudeAdjustments(
                    analysisResponse.Analysis,
                    filterContext,
                    marketContext,
                    marketStatus,
                    settings,
                    duplicateBuyBlocked: true,
                    logger);
                ApplyClaudeAnalysis(tradingSignal, analysisResponse);
                await SaveChangesAsync();
            }

            var sendTelegram = !duplicateBuyBlocked && !isTest && NotificationFilter.ShouldSendTelegram(settings, analysisResponse, marketStatus);
            var sendTestTelegram = !duplicateBuyBlocked && isTest && settings.SendTestTelegram;

            await TrySendTelegramAsync(
                tradingSignal,
                analysisResponse,
                marketStatus,
                isTest,
                sendTelegram,
                sendTestTelegram,
                duplicateBuyBlocked,
                workToken);

            await SaveChangesAsync();

            var allowPositions = !isTest || settings.AllowTestTrades;
            if (allowPositions)
            {
                await TryProcessPositionsAsync(
                    tradingSignal,
                    marketContext,
                    analysisResponse,
                    sendTelegram || sendTestTelegram,
                    isTest,
                    workToken);
            }
            else if (isTest)
            {
                logger.LogInformation(
                    "Paper position changes skipped for test signal {SignalId} because ALLOW_TEST_TRADES is disabled.",
                    tradingSignal.Id);
            }

            return await FinalizeSuccessAsync(webhookLog, tradingSignal, source, isTest);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Webhook processing failed for log {WebhookLogId}.", webhookLog.Id);
            return await FailAsync(webhookLog, WebhookResultStatuses.Error, "Webhook processing failed unexpectedly.");
        }
    }

    private async Task<WebhookProcessResponse> FinalizePreClaudeAsync(
        WebhookRequestLog webhookLog,
        TradingSignal tradingSignal,
        PreClaudeFilterResult preClaudeResult,
        string source,
        bool isTest)
    {
        tradingSignal.ClaudeAction = preClaudeResult.Decision;
        tradingSignal.ShortReason = preClaudeResult.Reason;
        tradingSignal.ReasonCategories = preClaudeResult.ReasonCategories;
        tradingSignal.IgnoredReason = preClaudeResult.Reason;
        tradingSignal.IgnoredBy = preClaudeResult.IgnoredBy;
        tradingSignal.ShouldNotify = false;
        tradingSignal.Notified = false;

        await SaveChangesAsync();
        await FinalizeWebhookLogAsync(webhookLog, WebhookResultStatuses.Success, null);

        logger.LogInformation(
            "Signal finalized before Claude. SignalId={SignalId}, Decision={Decision}, Categories={Categories}",
            tradingSignal.Id,
            preClaudeResult.Decision,
            preClaudeResult.ReasonCategories);

        return BuildResponse(success: true, webhookLog, tradingSignal, source, isTest);
    }

    private async Task<WebhookProcessResponse> FinalizeMarketIgnoredAsync(
        WebhookRequestLog webhookLog,
        TradingSignal tradingSignal,
        MarketStatusDto marketStatus,
        bool isTest,
        string source)
    {
        tradingSignal.ClaudeAction = "IGNORE";
        tradingSignal.ShortReason = "Market closed";
        tradingSignal.ReasonCategories = SignalReasonCategories.SessionRisk;
        tradingSignal.IgnoredReason = marketStatusService.GetIgnoredReason(marketStatus);
        tradingSignal.IgnoredBy = SignalIgnoredBy.MarketStatus;
        tradingSignal.ShouldNotify = false;
        tradingSignal.Notified = false;

        await SaveChangesAsync();

        if (settings.SendMarketClosedNotifications && !isTest)
        {
            try
            {
                await telegramNotificationService.SendMarketClosedAsync(tradingSignal, marketStatus, DbCancellation);
                tradingSignal.Notified = true;
                await SaveChangesAsync();
                logger.LogInformation("Market-closed Telegram sent. SignalId={SignalId}", tradingSignal.Id);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Market-closed Telegram failed. SignalId={SignalId}", tradingSignal.Id);
            }
        }

        logger.LogInformation(
            "Signal ignored due to market status. SignalId={SignalId}, Market={Market}, Status={Status}",
            tradingSignal.Id,
            marketStatus.MarketName,
            marketStatus.Status);

        await FinalizeWebhookLogAsync(webhookLog, WebhookResultStatuses.Ignored, null);

        return BuildResponse(
            success: true,
            webhookLog,
            tradingSignal,
            source,
            isTest,
            marketIgnored: true,
            marketStatus);
    }

    private async Task<WebhookProcessResponse> FinalizeAiFailureAsync(
        WebhookRequestLog webhookLog,
        TradingSignal tradingSignal,
        string source,
        bool isTest,
        string reason,
        string? claudeRawResponse = null)
    {
        tradingSignal.ClaudeAction = "IGNORE";
        tradingSignal.ShortReason = reason;
        tradingSignal.ClaudeRawResponse = claudeRawResponse;
        tradingSignal.ShouldNotify = false;
        tradingSignal.Notified = false;

        await SaveChangesAsync();
        await FinalizeWebhookLogAsync(webhookLog, WebhookResultStatuses.AiFailed, reason);

        logger.LogInformation(
            "Webhook finalized with AI failure. WebhookLogId={WebhookLogId}, SignalId={SignalId}, Reason={Reason}",
            webhookLog.Id,
            tradingSignal.Id,
            reason);

        return BuildResponse(success: true, webhookLog, tradingSignal, source, isTest);
    }

    private async Task<WebhookProcessResponse> FinalizeSuccessAsync(
        WebhookRequestLog webhookLog,
        TradingSignal tradingSignal,
        string source,
        bool isTest)
    {
        await FinalizeWebhookLogAsync(webhookLog, WebhookResultStatuses.Success, null);

        logger.LogInformation(
            "Webhook log finalized. WebhookLogId={WebhookLogId}, SignalId={SignalId}, Status={Status}",
            webhookLog.Id,
            tradingSignal.Id,
            WebhookResultStatuses.Success);

        return BuildResponse(success: true, webhookLog, tradingSignal, source, isTest);
    }

    private async Task TrySendTelegramAsync(
        TradingSignal tradingSignal,
        ClaudeAnalysisResponse analysisResponse,
        MarketStatusDto marketStatus,
        bool isTest,
        bool sendTelegram,
        bool sendTestTelegram,
        bool duplicateBuyBlocked,
        CancellationToken workToken)
    {
        try
        {
            if (sendTelegram)
            {
                await telegramNotificationService.SendSignalAsync(
                    tradingSignal,
                    analysisResponse.IsFallback,
                    isTest: false,
                    marketStatus,
                    workToken);
                tradingSignal.Notified = true;
                logger.LogInformation("Telegram sent. SignalId={SignalId}", tradingSignal.Id);
            }
            else if (sendTestTelegram)
            {
                await telegramNotificationService.SendSignalAsync(
                    tradingSignal,
                    analysisResponse.IsFallback,
                    isTest: true,
                    marketStatus,
                    workToken);
                tradingSignal.Notified = true;
                logger.LogInformation("Test Telegram sent. SignalId={SignalId}", tradingSignal.Id);
            }
            else if (duplicateBuyBlocked && settings.SendDuplicateBuyNotifications && !isTest)
            {
                await telegramNotificationService.SendDuplicateBuyAsync(tradingSignal, marketStatus, workToken);
                tradingSignal.Notified = true;
                logger.LogInformation("Duplicate-buy Telegram sent. SignalId={SignalId}", tradingSignal.Id);
            }
            else
            {
                tradingSignal.Notified = false;
                logger.LogInformation("Telegram skipped. SignalId={SignalId}", tradingSignal.Id);
            }
        }
        catch (Exception exception)
        {
            tradingSignal.Notified = false;
            logger.LogWarning(exception, "Telegram failed but webhook processing continues. SignalId={SignalId}", tradingSignal.Id);
        }
    }

    private async Task TryProcessPositionsAsync(
        TradingSignal tradingSignal,
        MarketContext marketContext,
        ClaudeAnalysisResponse analysisResponse,
        bool notificationPassed,
        bool isTest,
        CancellationToken workToken)
    {
        try
        {
            var positionResult = await positionManagerService.ProcessWebhookSignalAsync(
                tradingSignal,
                marketContext,
                analysisResponse,
                notificationPassed,
                workToken);

            if (!string.IsNullOrWhiteSpace(positionResult.SkippedReason))
            {
                tradingSignal.Notes = string.IsNullOrWhiteSpace(tradingSignal.Notes)
                    ? positionResult.SkippedReason
                    : $"{tradingSignal.Notes} | {positionResult.SkippedReason}";
                await SaveChangesAsync();
            }

            if (positionResult.OpenedPosition is not null && !isTest)
            {
                await telegramNotificationService.SendPositionOpenedAsync(positionResult.OpenedPosition, DbCancellation);
            }

            if (positionResult.ClosedPosition is not null && !isTest)
            {
                await telegramNotificationService.SendPositionClosedAsync(positionResult.ClosedPosition, DbCancellation);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Position processing failed but webhook processing continues. SignalId={SignalId}", tradingSignal.Id);
        }
    }

    private static void ApplyClaudeAnalysis(TradingSignal tradingSignal, ClaudeAnalysisResponse analysisResponse)
    {
        tradingSignal.ClaudeAction = analysisResponse.Analysis?.Action;
        tradingSignal.Confidence = analysisResponse.Analysis?.Confidence;
        tradingSignal.RiskLevel = analysisResponse.Analysis?.RiskLevel;
        tradingSignal.ClaudeRawResponse = analysisResponse.RawResponse;
        tradingSignal.ShortReason = analysisResponse.Analysis?.ShortReason;
        tradingSignal.SuggestedStopLoss = analysisResponse.Analysis?.SuggestedStopLoss;
        tradingSignal.SuggestedTakeProfit = analysisResponse.Analysis?.SuggestedTakeProfit;
        tradingSignal.RiskRewardRatio = analysisResponse.Analysis?.RiskRewardRatio;
        tradingSignal.PositionSizePercent = analysisResponse.Analysis?.PositionSizePercent;
        tradingSignal.ShouldNotify = analysisResponse.Analysis?.ShouldNotify;
        tradingSignal.ReasonCategories = analysisResponse.Analysis?.ReasonCategories;
    }

    private async Task<bool> TryBlockDuplicateBuyAsync(
        TradingSignal tradingSignal,
        ClaudeAnalysisResponse analysisResponse)
    {
        if (settings.AllowScaleIn)
        {
            return false;
        }

        if (!string.Equals(analysisResponse.Analysis?.Action, "BUY", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var openPositionCount = await dbContext.Positions.CountAsync(
            position => position.Symbol == tradingSignal.Symbol && position.Status == PositionStatus.Open,
            DbCancellation);

        if (openPositionCount < settings.MaxPositionsPerSymbol)
        {
            return false;
        }

        tradingSignal.IgnoredReason = "Position already open";
        tradingSignal.IgnoredBy = SignalIgnoredBy.PositionRules;
        tradingSignal.ShouldNotify = false;
        tradingSignal.ClaudeAction = "IGNORE";
        tradingSignal.ReasonCategories = SignalReasonCategories.DuplicateBuy;

        if (string.IsNullOrWhiteSpace(tradingSignal.ShortReason))
        {
            tradingSignal.ShortReason = "Position already open";
        }

        await SaveChangesAsync();

        logger.LogInformation(
            "Signal {SignalId} blocked by position rules. Symbol={Symbol}, OpenPositions={Count}",
            tradingSignal.Id,
            tradingSignal.Symbol,
            openPositionCount);

        return true;
    }

    private async Task FinalizeWebhookLogAsync(WebhookRequestLog webhookLog, string status, string? errorMessage)
    {
        webhookLog.ResultStatus = status;
        webhookLog.ErrorMessage = errorMessage;
        await SaveChangesAsync();
    }

    private async Task<WebhookProcessResponse> FailAsync(
        WebhookRequestLog webhookLog,
        string status,
        string error)
    {
        await FinalizeWebhookLogAsync(webhookLog, status, error);

        return new WebhookProcessResponse
        {
            Success = false,
            WebhookLogId = webhookLog.Id,
            Source = webhookLog.Source,
            IsTest = webhookLog.IsTest,
            ResultStatus = status,
            Error = error
        };
    }

    private static WebhookProcessResponse BuildResponse(
        bool success,
        WebhookRequestLog webhookLog,
        TradingSignal tradingSignal,
        string source,
        bool isTest,
        bool marketIgnored = false,
        MarketStatusDto? marketStatus = null)
        => new()
        {
            Success = success,
            WebhookLogId = webhookLog.Id,
            SignalId = tradingSignal.Id,
            Source = source,
            IsTest = isTest,
            ResultStatus = webhookLog.ResultStatus,
            Signal = tradingSignal,
            MarketIgnored = marketIgnored,
            MarketStatus = marketStatus
        };

    private Task SaveChangesAsync()
        => dbContext.SaveChangesAsync(DbCancellation);

    private static string SerializeHeaders(IHeaderDictionary headers)
    {
        var sanitized = new Dictionary<string, string>();
        foreach (var header in headers)
        {
            var key = header.Key;
            var value = header.Value.ToString();
            if (key.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || key.Contains("authorization", StringComparison.OrdinalIgnoreCase))
            {
                value = "***";
            }

            sanitized[key] = value;
        }

        return JsonSerializer.Serialize(sanitized);
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue)
            ? parsedValue
            : null;
    }
}
