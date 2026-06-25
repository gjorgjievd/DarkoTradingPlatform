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

    public async Task SendSignalAsync(TradingSignal signal, bool usedFallback, bool isTest, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.TelegramBotToken) || string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            logger.LogWarning("Telegram notification skipped because bot configuration is missing.");
            return;
        }

        var message = BuildMessage(signal, usedFallback);
        if (isTest)
        {
            message = $"🧪 TEST\n{message}";
        }

        await SendMessageAsync(message, cancellationToken);
    }

    public async Task SendPositionOpenedAsync(Position position, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.TelegramBotToken) || string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            logger.LogWarning("Telegram position-open notification skipped because bot configuration is missing.");
            return;
        }

        var message = new StringBuilder();
        message.AppendLine("📥 Paper Position Opened");
        message.AppendLine();
        message.AppendLine($"Symbol: {position.Symbol}");
        message.AppendLine($"Entry Price: {FormatDecimal(position.EntryPrice)}");
        message.AppendLine($"Quantity: {FormatDecimal(position.Quantity)}");
        message.AppendLine($"Entry Time: {position.EntryTimeUtc:u}");
        if (position.MaxRiskPercent.HasValue)
        {
            message.AppendLine($"Max Risk: {FormatDecimal(position.MaxRiskPercent)}%");
        }

        await SendMessageAsync(message.ToString(), cancellationToken);
    }

    public async Task SendPositionClosedAsync(Position position, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.TelegramBotToken) || string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            logger.LogWarning("Telegram position-close notification skipped because bot configuration is missing.");
            return;
        }

        var message = new StringBuilder();
        message.AppendLine("📤 Paper Position Closed");
        message.AppendLine();
        message.AppendLine($"Symbol: {position.Symbol}");
        message.AppendLine($"Entry Price: {FormatDecimal(position.EntryPrice)}");
        message.AppendLine($"Exit Price: {FormatDecimal(position.ExitPrice)}");
        message.AppendLine();
        message.AppendLine("P/L:");
        message.AppendLine(FormatDecimal(position.ProfitLoss));
        message.AppendLine();
        message.AppendLine("P/L %:");
        message.AppendLine(position.ProfitLossPercent.HasValue ? $"{FormatDecimal(position.ProfitLossPercent)}%" : "N/A");

        await SendMessageAsync(message.ToString(), cancellationToken);
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
        var market = signal.MarketData;
        var builder = new StringBuilder();
        builder.AppendLine("📈 Trading Signal");
        builder.AppendLine();
        builder.AppendLine($"Symbol: {signal.Symbol}");
        builder.AppendLine($"Timeframe: {signal.Timeframe ?? "N/A"}");
        builder.AppendLine();
        builder.AppendLine($"TradingView Signal: {signal.OriginalSignal}");
        builder.AppendLine();
        builder.AppendLine($"Claude Decision: {signal.ClaudeAction ?? "N/A"}");
        builder.AppendLine();
        builder.AppendLine($"Confidence: {(signal.Confidence.HasValue ? $"{signal.Confidence.Value}%" : "N/A")}");
        builder.AppendLine();
        builder.AppendLine($"Risk: {signal.RiskLevel ?? "N/A"}");
        builder.AppendLine();
        builder.AppendLine($"RSI: {FormatDecimal(market?.Rsi14)}");
        builder.AppendLine();
        builder.AppendLine("EMA:");
        builder.AppendLine($"  9: {FormatDecimal(market?.Ema9)}");
        builder.AppendLine($"  20: {FormatDecimal(market?.Ema20)}");
        builder.AppendLine($"  50: {FormatDecimal(market?.Ema50)}");
        builder.AppendLine();
        builder.AppendLine("Volume:");
        builder.AppendLine($"  Current: {FormatVolume(market?.CurrentVolume)}");
        builder.AppendLine($"  Avg 20: {FormatVolume(market?.AverageVolume20)}");
        builder.AppendLine($"  Spike: {FormatVolumeSpike(market?.CurrentVolume, market?.AverageVolume20)}");
        builder.AppendLine();
        builder.AppendLine("Stop Loss:");
        builder.AppendLine(FormatDecimal(signal.SuggestedStopLoss));
        builder.AppendLine();
        builder.AppendLine("Take Profit:");
        builder.AppendLine(FormatDecimal(signal.SuggestedTakeProfit));
        builder.AppendLine();
        builder.AppendLine($"Risk/Reward: {FormatDecimal(signal.RiskRewardRatio)}");
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

    private static string FormatVolume(long? value)
        => value.HasValue ? value.Value.ToString("N0", CultureInfo.InvariantCulture) : "N/A";

    private static string FormatVolumeSpike(long? currentVolume, long? averageVolume)
    {
        if (!currentVolume.HasValue || !averageVolume.HasValue || averageVolume.Value == 0)
        {
            return "N/A";
        }

        var spikePercent = ((decimal)currentVolume.Value / averageVolume.Value - 1m) * 100m;
        return $"{spikePercent:0.#}%";
    }
}
