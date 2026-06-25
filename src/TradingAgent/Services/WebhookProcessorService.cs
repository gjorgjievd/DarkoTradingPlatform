using System.Globalization;
using System.Text.Json;
using TradingAgent.Configuration;
using TradingAgent.Data;
using TradingAgent.DTOs;
using TradingAgent.Models;

namespace TradingAgent.Services;

public interface IWebhookProcessorService
{
    Task<WebhookProcessResponse> ProcessAsync(
        HttpRequest request,
        string rawPayload,
        bool forceTest,
        CancellationToken cancellationToken);
}

public sealed class WebhookProcessorService(
    TradingAgentDbContext dbContext,
    IClaudeAnalysisService claudeAnalysisService,
    ITelegramNotificationService telegramNotificationService,
    IYahooFinanceService yahooFinanceService,
    IPositionManagerService positionManagerService,
    AppSettings settings,
    ILogger<WebhookProcessorService> logger) : IWebhookProcessorService
{
    public async Task<WebhookProcessResponse> ProcessAsync(
        HttpRequest request,
        string rawPayload,
        bool forceTest,
        CancellationToken cancellationToken)
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
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            if (string.IsNullOrWhiteSpace(rawPayload))
            {
                return await FailAsync(webhookLog, "BAD_REQUEST", "Request body is required.", cancellationToken);
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
                return await FailAsync(webhookLog, "BAD_REQUEST", "Invalid JSON payload.", cancellationToken);
            }

            var (source, isTest) = forceTest
                ? (WebhookSources.CursorTest, true)
                : WebhookSourceDetector.Detect(request, rawPayload, payload);

            webhookLog.Source = source;
            webhookLog.IsTest = isTest;
            await dbContext.SaveChangesAsync(cancellationToken);

            if (payload is null || string.IsNullOrWhiteSpace(payload.Symbol) || string.IsNullOrWhiteSpace(payload.Signal))
            {
                return await FailAsync(webhookLog, "BAD_REQUEST", "Payload must include symbol and signal.", cancellationToken);
            }

            var headerSecret = request.Headers["X-Webhook-Secret"].FirstOrDefault()
                ?? request.Headers["X-TradingView-Secret"].FirstOrDefault();

            var providedSecret = string.IsNullOrWhiteSpace(payload.Secret) ? headerSecret : payload.Secret;
            if (!string.Equals(providedSecret, settings.WebhookSecret, StringComparison.Ordinal))
            {
                logger.LogWarning("Rejected webhook for symbol {Symbol} because of an invalid secret.", payload.Symbol);
                return await FailAsync(webhookLog, "UNAUTHORIZED", "Invalid webhook secret.", cancellationToken);
            }

            logger.LogInformation(
                "Webhook received. Source={Source}, IsTest={IsTest}, Symbol={Symbol}, Signal={Signal}",
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
            await dbContext.SaveChangesAsync(cancellationToken);

            webhookLog.TradingSignalId = tradingSignal.Id;
            await dbContext.SaveChangesAsync(cancellationToken);

            var marketContext = await yahooFinanceService.FetchMarketContextAsync(tradingSignal.Symbol, cancellationToken);
            var marketDataEntity = MarketContextMapper.ToEntity(marketContext, tradingSignal.Id);
            dbContext.SignalMarketData.Add(marketDataEntity);
            await dbContext.SaveChangesAsync(cancellationToken);
            tradingSignal.MarketData = marketDataEntity;

            var analysisResponse = await claudeAnalysisService.AnalyzeAsync(payload, marketContext, cancellationToken);

            tradingSignal.ClaudeAction = analysisResponse.Analysis?.Action;
            tradingSignal.Confidence = analysisResponse.Analysis?.Confidence;
            tradingSignal.RiskLevel = analysisResponse.Analysis?.RiskLevel;
            tradingSignal.ClaudeRawResponse = analysisResponse.RawResponse;
            tradingSignal.ShortReason = analysisResponse.IsFallback
                ? analysisResponse.Error ?? "Claude unavailable, fallback mode."
                : analysisResponse.Analysis?.ShortReason;
            tradingSignal.SuggestedStopLoss = analysisResponse.Analysis?.SuggestedStopLoss;
            tradingSignal.SuggestedTakeProfit = analysisResponse.Analysis?.SuggestedTakeProfit;
            tradingSignal.RiskRewardRatio = analysisResponse.Analysis?.RiskRewardRatio;
            tradingSignal.PositionSizePercent = analysisResponse.Analysis?.PositionSizePercent;
            tradingSignal.ShouldNotify = analysisResponse.Analysis?.ShouldNotify;

            await dbContext.SaveChangesAsync(cancellationToken);

            var sendTelegram = !isTest && NotificationFilter.ShouldSendTelegram(settings, analysisResponse);
            var sendTestTelegram = isTest && settings.SendTestTelegram;

            if (sendTelegram)
            {
                await telegramNotificationService.SendSignalAsync(tradingSignal, analysisResponse.IsFallback, isTest: false, cancellationToken);
                tradingSignal.Notified = true;
            }
            else if (sendTestTelegram)
            {
                await telegramNotificationService.SendSignalAsync(tradingSignal, analysisResponse.IsFallback, isTest: true, cancellationToken);
                tradingSignal.Notified = true;
            }
            else
            {
                tradingSignal.Notified = false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var allowPositions = !isTest || settings.AllowTestTrades;
            PositionActionResult? positionResult = null;

            if (allowPositions)
            {
                positionResult = await positionManagerService.ProcessWebhookSignalAsync(
                    tradingSignal,
                    marketContext,
                    analysisResponse,
                    sendTelegram || sendTestTelegram,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(positionResult.SkippedReason))
                {
                    tradingSignal.Notes = string.IsNullOrWhiteSpace(tradingSignal.Notes)
                        ? positionResult.SkippedReason
                        : $"{tradingSignal.Notes} | {positionResult.SkippedReason}";
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                if (positionResult.OpenedPosition is not null && !isTest)
                {
                    await telegramNotificationService.SendPositionOpenedAsync(positionResult.OpenedPosition, cancellationToken);
                }

                if (positionResult.ClosedPosition is not null && !isTest)
                {
                    await telegramNotificationService.SendPositionClosedAsync(positionResult.ClosedPosition, cancellationToken);
                }
            }
            else if (isTest)
            {
                logger.LogInformation(
                    "Paper position changes skipped for test signal {SignalId} because ALLOW_TEST_TRADES is disabled.",
                    tradingSignal.Id);
            }

            webhookLog.ResultStatus = "SUCCESS";
            webhookLog.ErrorMessage = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            return new WebhookProcessResponse
            {
                Success = true,
                WebhookLogId = webhookLog.Id,
                SignalId = tradingSignal.Id,
                Source = source,
                IsTest = isTest,
                ResultStatus = webhookLog.ResultStatus,
                Signal = tradingSignal
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Webhook processing failed for log {WebhookLogId}.", webhookLog.Id);
            return await FailAsync(webhookLog, "ERROR", "Webhook processing failed unexpectedly.", cancellationToken);
        }
    }

    private async Task<WebhookProcessResponse> FailAsync(
        WebhookRequestLog webhookLog,
        string status,
        string error,
        CancellationToken cancellationToken)
    {
        webhookLog.ResultStatus = status;
        webhookLog.ErrorMessage = error;
        await dbContext.SaveChangesAsync(cancellationToken);

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
