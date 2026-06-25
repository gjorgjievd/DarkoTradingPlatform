using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingAgent.Configuration;
using TradingAgent.Models;

namespace TradingAgent.Services;

public sealed class TelegramNotificationService(
    IHttpClientFactory httpClientFactory,
    AppSettings settings,
    ILogger<TelegramNotificationService> logger) : ITelegramNotificationService
{
    public const string HttpClientName = "telegram";

    public async Task SendSignalAsync(TradingSignal signal, bool usedFallback, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.TelegramBotToken) || string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            logger.LogWarning("Telegram notification skipped because bot configuration is missing.");
            return;
        }

        var message = BuildMessage(signal, usedFallback);
        await SendMessageAsync(message, cancellationToken);
    }

    public async Task<TelegramTestResult> SendTestAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.TelegramBotToken) || string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            return new TelegramTestResult
            {
                Success = false,
                Error = "Telegram bot token or chat ID is not configured."
            };
        }

        const string message = "✅ TradingAgent Telegram test message — connection OK.";
        return await SendMessageAsync(message, cancellationToken);
    }

    private async Task<TelegramTestResult> SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var requestUri = $"https://api.telegram.org/bot{settings.TelegramBotToken}/sendMessage";

        var payload = new
        {
            chat_id = settings.TelegramChatId,
            text = message
        };

        try
        {
            using var response = await client.PostAsync(
                requestUri,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Telegram message sent successfully. StatusCode={StatusCode}", (int)response.StatusCode);
                return new TelegramTestResult
                {
                    Success = true,
                    HttpStatusCode = (int)response.StatusCode
                };
            }

            logger.LogWarning(
                "Telegram notification failed. StatusCode={StatusCode}, Response={Response}",
                (int)response.StatusCode,
                responseBody.Length > 500 ? responseBody[..500] : responseBody);

            return new TelegramTestResult
            {
                Success = false,
                HttpStatusCode = (int)response.StatusCode,
                Error = $"Telegram request failed with status {(int)response.StatusCode}."
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Telegram notification failed unexpectedly.");
            return new TelegramTestResult
            {
                Success = false,
                Error = "Telegram notification failed unexpectedly."
            };
        }
    }

    private static string BuildMessage(TradingSignal signal, bool usedFallback)
    {
        var builder = new StringBuilder();
        builder.AppendLine("📈 Trading Signal");
        builder.AppendLine();
        builder.AppendLine($"Symbol: {signal.Symbol}");
        builder.AppendLine($"Timeframe: {signal.Timeframe ?? "N/A"}");
        builder.AppendLine();
        builder.AppendLine($"Original Signal: {signal.OriginalSignal}");
        builder.AppendLine();
        builder.AppendLine($"Claude Decision: {signal.ClaudeAction ?? "N/A"}");
        builder.AppendLine();
        builder.AppendLine($"Confidence: {(signal.Confidence.HasValue ? $"{signal.Confidence.Value}%" : "N/A")}");
        builder.AppendLine();
        builder.AppendLine($"Risk: {signal.RiskLevel ?? "N/A"}");
        builder.AppendLine();
        builder.AppendLine("Entry:");
        builder.AppendLine(FormatDecimal(signal.Price));
        builder.AppendLine();
        builder.AppendLine("Stop Loss:");
        builder.AppendLine(FormatDecimal(signal.SuggestedStopLoss));
        builder.AppendLine();
        builder.AppendLine("Take Profit:");
        builder.AppendLine(FormatDecimal(signal.SuggestedTakeProfit));
        builder.AppendLine();
        builder.AppendLine("Reason:");

        if (usedFallback)
        {
            builder.AppendLine("Claude unavailable, fallback mode.");
        }
        else
        {
            builder.AppendLine(signal.ShortReason ?? "No reason provided.");
        }

        return builder.ToString();
    }

    private static string FormatDecimal(decimal? value)
        => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : "N/A";
}
