namespace TradingAgent.Configuration;

public sealed class AppSettings
{
    public string TelegramBotToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;
    public string ClaudeApiKey { get; set; } = string.Empty;
    public string ClaudeModel { get; set; } = "claude-haiku-4-5-20251001";
    public bool ClaudeEnabled { get; set; } = true;
    public string DatabasePath { get; set; } = "/app/data/tradingagent.db";
    public int RetentionDays { get; set; } = 30;
    public string WebhookSecret { get; set; } = string.Empty;
    public int MinConfidenceToNotify { get; set; } = 70;
    public bool SendIgnoredSignals { get; set; }
    public bool PaperTradingEnabled { get; set; } = true;
    public decimal DefaultPositionQuantity { get; set; } = 1;
    public bool AllowTestTrades { get; set; }
    public bool SendTestTelegram { get; set; }
}
