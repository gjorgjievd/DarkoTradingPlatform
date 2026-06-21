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

        var client = httpClientFactory.CreateClient(HttpClientName);
        var requestUri = $"https://api.telegram.org/bot{settings.TelegramBotToken}/sendMessage";
        var message = BuildMessage(signal, usedFallback);

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

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Telegram notification failed with status code {StatusCode}.", (int)response.StatusCode);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Telegram notification failed unexpectedly.");
        }
    }

    private static string BuildMessage(TradingSignal signal, bool usedFallback)
    {
        var builder = new StringBuilder();
        builder.AppendLine("📊 Trading Signal");
        builder.AppendLine();
        builder.AppendLine($"Symbol: {signal.Symbol}");
        builder.AppendLine($"Original Signal: {signal.OriginalSignal}");
        builder.AppendLine($"Claude Action: {signal.ClaudeAction ?? "N/A"}");
        builder.AppendLine($"Confidence: {(signal.Confidence.HasValue ? $"{signal.Confidence.Value}%" : "N/A")}");
        builder.AppendLine($"Risk: {signal.RiskLevel ?? "N/A"}");
        builder.AppendLine($"Price: {FormatDecimal(signal.Price)}");
        builder.AppendLine($"Timeframe: {signal.Timeframe ?? "N/A"}");
        builder.AppendLine();
        builder.AppendLine("Reason:");
        builder.AppendLine(signal.ShortReason ?? "Claude analysis unavailable.");
        builder.AppendLine();
        builder.AppendLine($"Stop Loss: {FormatDecimal(signal.SuggestedStopLoss)}");
        builder.AppendLine($"Take Profit: {FormatDecimal(signal.SuggestedTakeProfit)}");

        if (usedFallback)
        {
            builder.AppendLine();
            builder.AppendLine("Status: Fallback mode used.");
        }

        return builder.ToString();
    }

    private static string FormatDecimal(decimal? value)
        => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : "N/A";
}
