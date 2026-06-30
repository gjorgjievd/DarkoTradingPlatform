using Microsoft.EntityFrameworkCore;
using TradingAgent.Configuration;
using TradingAgent.Data;
using TradingAgent.DTOs;
using TradingAgent.Models;

namespace TradingAgent.Services;

public interface IRuntimeSettingsService
{
    Task ApplyDatabaseOverridesAsync(CancellationToken cancellationToken = default);
    SettingsResponse GetSettings();
    Task<SettingsResponse> UpdateSettingsAsync(
        IReadOnlyDictionary<string, string> updates,
        CancellationToken cancellationToken = default);
}

public sealed class RuntimeSettingsService(
    AppSettings settings,
    IServiceScopeFactory scopeFactory) : IRuntimeSettingsService
{
    private static readonly IReadOnlyDictionary<string, SettingDefinition> Definitions =
        new Dictionary<string, SettingDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["CLAUDE_ENABLED"] = SettingDefinition.Bool(s => s.ClaudeEnabled, (s, v) => s.ClaudeEnabled = v),
            ["CLAUDE_MODEL"] = SettingDefinition.String(s => s.ClaudeModel, (s, v) => s.ClaudeModel = v, maxLength: 128),
            ["CLAUDE_TIMEOUT_SECONDS"] = SettingDefinition.Int(s => s.ClaudeTimeoutSeconds, (s, v) => s.ClaudeTimeoutSeconds = v, 10, 300),
            ["CLAUDE_MAX_RETRIES"] = SettingDefinition.Int(s => s.ClaudeMaxRetries, (s, v) => s.ClaudeMaxRetries = v, 0, 5),
            ["MIN_CONFIDENCE_TO_NOTIFY"] = SettingDefinition.Int(s => s.MinConfidenceToNotify, (s, v) => s.MinConfidenceToNotify = v, 0, 100),
            ["MIN_CONFIDENCE_REGULAR"] = SettingDefinition.Int(s => s.MinConfidenceRegular, (s, v) => s.MinConfidenceRegular = v, 0, 100),
            ["MIN_CONFIDENCE_PREMARKET"] = SettingDefinition.Int(s => s.MinConfidencePremarket, (s, v) => s.MinConfidencePremarket = v, 0, 100),
            ["MIN_CONFIDENCE_AFTER_HOURS"] = SettingDefinition.Int(s => s.MinConfidenceAfterHours, (s, v) => s.MinConfidenceAfterHours = v, 0, 100),
            ["MIN_CONFIDENCE_OVERNIGHT"] = SettingDefinition.Int(s => s.MinConfidenceOvernight, (s, v) => s.MinConfidenceOvernight = v, 0, 100),
            ["ENABLE_24_5_TRADING"] = SettingDefinition.Bool(s => s.Enable24_5Trading, (s, v) => s.Enable24_5Trading = v),
            ["ALLOW_PREMARKET"] = SettingDefinition.Bool(s => s.AllowPreMarket, (s, v) => s.AllowPreMarket = v),
            ["ALLOW_AFTER_HOURS"] = SettingDefinition.Bool(s => s.AllowAfterHours, (s, v) => s.AllowAfterHours = v),
            ["ALLOW_OVERNIGHT"] = SettingDefinition.Bool(s => s.AllowOvernight, (s, v) => s.AllowOvernight = v),
            ["PAPER_TRADING_ENABLED"] = SettingDefinition.Bool(s => s.PaperTradingEnabled, (s, v) => s.PaperTradingEnabled = v),
            ["ALLOW_SCALE_IN"] = SettingDefinition.Bool(s => s.AllowScaleIn, (s, v) => s.AllowScaleIn = v),
            ["MAX_POSITIONS_PER_SYMBOL"] = SettingDefinition.Int(s => s.MaxPositionsPerSymbol, (s, v) => s.MaxPositionsPerSymbol = v, 1, 20),
            ["SEND_IGNORED_SIGNALS"] = SettingDefinition.Bool(s => s.SendIgnoredSignals, (s, v) => s.SendIgnoredSignals = v),
            ["SEND_WAIT_SIGNALS"] = SettingDefinition.Bool(s => s.SendWaitSignals, (s, v) => s.SendWaitSignals = v),
            ["MAX_PRICE_DRIFT_PERCENT_REGULAR"] = SettingDefinition.Decimal(
                s => s.MaxPriceDriftPercentRegular,
                (s, v) => s.MaxPriceDriftPercentRegular = v,
                0.1m,
                10m),
            ["MAX_PRICE_DRIFT_PERCENT_EXTENDED"] = SettingDefinition.Decimal(
                s => s.MaxPriceDriftPercentExtended,
                (s, v) => s.MaxPriceDriftPercentExtended = v,
                0.1m,
                15m),
            ["SEND_TEST_TELEGRAM"] = SettingDefinition.Bool(s => s.SendTestTelegram, (s, v) => s.SendTestTelegram = v),
            ["MARKET_PROVIDER"] = SettingDefinition.String(s => s.MarketProvider, (s, v) => s.MarketProvider = v, maxLength: 32),
            ["MARKET_TIMEZONE"] = SettingDefinition.String(s => s.MarketTimezone, (s, v) => s.MarketTimezone = v, maxLength: 64),
            ["WEBHOOK_SECRET"] = SettingDefinition.String(s => s.WebhookSecret, (s, v) => s.WebhookSecret = v, maxLength: 256, isSecret: true)
        };

    public async Task ApplyDatabaseOverridesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradingAgentDbContext>();
        var rows = await dbContext.RuntimeSettings.AsNoTracking().ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (Definitions.TryGetValue(row.Key, out var definition))
            {
                definition.Apply(settings, row.Value);
            }
        }
    }

    public SettingsResponse GetSettings()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradingAgentDbContext>();
        var overriddenKeys = dbContext.RuntimeSettings
            .AsNoTracking()
            .Select(row => row.Key)
            .ToList();

        return BuildResponse(overriddenKeys);
    }

    public async Task<SettingsResponse> UpdateSettingsAsync(
        IReadOnlyDictionary<string, string> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0)
        {
            throw new ArgumentException("At least one setting must be provided.");
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradingAgentDbContext>();

        foreach (var (rawKey, rawValue) in updates)
        {
            var key = NormalizeKey(rawKey);
            if (!Definitions.TryGetValue(key, out var definition))
            {
                throw new ArgumentException($"Setting '{rawKey}' cannot be changed at runtime.");
            }

            if (definition.IsSecret && IsMaskedSecretValue(rawValue))
            {
                continue;
            }

            var normalizedValue = definition.Normalize(rawValue);
            definition.Apply(settings, normalizedValue);

            var existing = await dbContext.RuntimeSettings.FirstOrDefaultAsync(row => row.Key == key, cancellationToken);
            if (existing is null)
            {
                dbContext.RuntimeSettings.Add(new RuntimeSetting
                {
                    Key = key,
                    Value = normalizedValue,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.Value = normalizedValue;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var overriddenKeys = await dbContext.RuntimeSettings
            .AsNoTracking()
            .Select(row => row.Key)
            .ToListAsync(cancellationToken);

        return BuildResponse(overriddenKeys);
    }

    private SettingsResponse BuildResponse(IReadOnlyList<string> overriddenKeys)
    {
        var overridden = new HashSet<string>(overriddenKeys, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["MIN_CONFIDENCE_TO_NOTIFY"] = settings.MinConfidenceToNotify,
            ["SEND_IGNORED_SIGNALS"] = settings.SendIgnoredSignals,
            ["SEND_WAIT_SIGNALS"] = settings.SendWaitSignals,
            ["MAX_PRICE_DRIFT_PERCENT_REGULAR"] = settings.MaxPriceDriftPercentRegular,
            ["MAX_PRICE_DRIFT_PERCENT_EXTENDED"] = settings.MaxPriceDriftPercentExtended,
            ["CLAUDE_ENABLED"] = settings.ClaudeEnabled,
            ["CLAUDE_MODEL"] = settings.ClaudeModel,
            ["CLAUDE_TIMEOUT_SECONDS"] = settings.ClaudeTimeoutSeconds,
            ["CLAUDE_MAX_RETRIES"] = settings.ClaudeMaxRetries,
            ["PAPER_TRADING_ENABLED"] = settings.PaperTradingEnabled,
            ["DEFAULT_POSITION_QUANTITY"] = settings.DefaultPositionQuantity,
            ["ALLOW_TEST_TRADES"] = settings.AllowTestTrades,
            ["SEND_TEST_TELEGRAM"] = settings.SendTestTelegram,
            ["MARKET_PROVIDER"] = settings.MarketProvider,
            ["MARKET_TIMEZONE"] = settings.MarketTimezone,
            ["ALLOW_PREMARKET"] = settings.AllowPreMarket,
            ["ALLOW_AFTER_HOURS"] = settings.AllowAfterHours,
            ["ALLOW_OVERNIGHT"] = settings.AllowOvernight,
            ["IGNORE_SIGNALS_WHEN_MARKET_CLOSED"] = settings.IgnoreSignalsWhenMarketClosed,
            ["SEND_MARKET_CLOSED_NOTIFICATIONS"] = settings.SendMarketClosedNotifications,
            ["ENABLE_24_5_TRADING"] = settings.Enable24_5Trading,
            ["MIN_CONFIDENCE_REGULAR"] = settings.MinConfidenceRegular,
            ["MIN_CONFIDENCE_PREMARKET"] = settings.MinConfidencePremarket,
            ["MIN_CONFIDENCE_AFTER_HOURS"] = settings.MinConfidenceAfterHours,
            ["MIN_CONFIDENCE_OVERNIGHT"] = settings.MinConfidenceOvernight,
            ["ALLOW_SCALE_IN"] = settings.AllowScaleIn,
            ["MAX_POSITIONS_PER_SYMBOL"] = settings.MaxPositionsPerSymbol,
            ["SEND_DUPLICATE_BUY_NOTIFICATIONS"] = settings.SendDuplicateBuyNotifications,
            ["WEBHOOK_SECRET"] = MaskWebhookSecret(settings.WebhookSecret)
        };

        return new SettingsResponse
        {
            Values = values,
            OverriddenKeys = overriddenKeys,
            EditableKeys = Definitions.Keys.OrderBy(key => key).ToList()
        };
    }

    public static string NormalizeKey(string key)
        => key.Trim().ToUpperInvariant();

    private static bool IsMaskedSecretValue(string value)
        => string.IsNullOrWhiteSpace(value)
           || value.Contains('*', StringComparison.Ordinal)
           || string.Equals(value, "(not set)", StringComparison.OrdinalIgnoreCase);

    private static string MaskWebhookSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return "(not set)";
        }

        if (secret.Length <= 4)
        {
            return "****";
        }

        return $"{secret[..2]}{new string('*', Math.Min(secret.Length - 3, 8))}{secret[^1]}";
    }

    private sealed class SettingDefinition
    {
        private readonly Action<AppSettings, string> _apply;

        private SettingDefinition(Action<AppSettings, string> apply, bool isSecret = false)
        {
            _apply = apply;
            IsSecret = isSecret;
        }

        public bool IsSecret { get; }

        public void Apply(AppSettings target, string value) => _apply(target, Normalize(value));

        public string Normalize(string value) => value.Trim();

        public static SettingDefinition Bool(
            Func<AppSettings, bool> _,
            Action<AppSettings, bool> setter)
            => new((target, value) =>
            {
                if (!bool.TryParse(value, out var parsed))
                {
                    throw new ArgumentException($"Invalid boolean value '{value}'.");
                }

                setter(target, parsed);
            });

        public static SettingDefinition Int(
            Func<AppSettings, int> _,
            Action<AppSettings, int> setter,
            int min,
            int max)
            => new((target, value) =>
            {
                if (!int.TryParse(value, out var parsed))
                {
                    throw new ArgumentException($"Invalid integer value '{value}'.");
                }

                if (parsed < min || parsed > max)
                {
                    throw new ArgumentException($"Value must be between {min} and {max}.");
                }

                setter(target, parsed);
            });

        public static SettingDefinition String(
            Func<AppSettings, string> _,
            Action<AppSettings, string> setter,
            int maxLength,
            bool isSecret = false)
            => new((target, value) =>
            {
                var normalized = value.Trim();
                if (normalized.Length == 0)
                {
                    throw new ArgumentException("Value cannot be empty.");
                }

                if (normalized.Length > maxLength)
                {
                    throw new ArgumentException($"Value cannot exceed {maxLength} characters.");
                }

                setter(target, normalized);
            }, isSecret);

        public static SettingDefinition Decimal(
            Func<AppSettings, decimal> _,
            Action<AppSettings, decimal> setter,
            decimal min,
            decimal max)
            => new((target, value) =>
            {
                if (!decimal.TryParse(value, out var parsed))
                {
                    throw new ArgumentException($"Invalid decimal value '{value}'.");
                }

                if (parsed < min || parsed > max)
                {
                    throw new ArgumentException($"Value must be between {min} and {max}.");
                }

                setter(target, parsed);
            });
    }
}
