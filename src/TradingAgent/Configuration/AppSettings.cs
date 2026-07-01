namespace TradingAgent.Configuration;

public sealed class AppSettings
{
    public string TelegramBotToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;
    public string ClaudeApiKey { get; set; } = string.Empty;
    public string ClaudeModel { get; set; } = "claude-haiku-4-5-20251001";
    public bool ClaudeEnabled { get; set; } = true;
    public int ClaudeTimeoutSeconds { get; set; } = 60;
    public int ClaudeMaxRetries { get; set; } = 1;
    public string DatabasePath { get; set; } = "/app/data/tradingagent.db";
    public int RetentionDays { get; set; } = 30;
    public string WebhookSecret { get; set; } = string.Empty;
    public int MinConfidenceToNotify { get; set; } = 70;
    public bool SendIgnoredSignals { get; set; }
    public bool SendWaitSignals { get; set; }
    public decimal MaxPriceDriftPercentRegular { get; set; } = 1.0m;
    public decimal MaxPriceDriftPercentExtended { get; set; } = 2.5m;
    public bool PaperTradingEnabled { get; set; } = true;
    public decimal DefaultPositionQuantity { get; set; } = 1;
    public bool AllowTestTrades { get; set; }
    public bool SendTestTelegram { get; set; }
    public string MarketProvider { get; set; } = "NASDAQ";
    public string MarketTimezone { get; set; } = "America/New_York";
    public bool AllowPreMarket { get; set; }
    public bool AllowAfterHours { get; set; }
    public bool AllowOvernight { get; set; }
    public bool IgnoreSignalsWhenMarketClosed { get; set; } = true;
    public bool SendMarketClosedNotifications { get; set; }
    public bool Enable24_5Trading { get; set; } = true;
    public int MinConfidenceRegular { get; set; } = 60;
    public int MinConfidencePremarket { get; set; } = 70;
    public int MinConfidenceAfterHours { get; set; } = 70;
    public int MinConfidenceOvernight { get; set; } = 75;
    public bool AllowScaleIn { get; set; }
    public int MaxPositionsPerSymbol { get; set; } = 1;
    public bool SendDuplicateBuyNotifications { get; set; }
}
