using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TradingAgent.BackgroundServices;
using TradingAgent.Configuration;
using TradingAgent.Data;
using TradingAgent.DTOs;
using TradingAgent.Models;
using TradingAgent.Services;
using TradingAgent.Services.Market;

DotEnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();

    return new AppSettings
    {
        TelegramBotToken = configuration["TELEGRAM_BOT_TOKEN"] ?? string.Empty,
        TelegramChatId = configuration["TELEGRAM_CHAT_ID"] ?? string.Empty,
        ClaudeApiKey = ResolveClaudeApiKey(configuration),
        ClaudeModel = configuration["CLAUDE_MODEL"] ?? "claude-haiku-4-5-20251001",
        ClaudeEnabled = bool.TryParse(configuration["CLAUDE_ENABLED"], out var enabled) ? enabled : true,
        ClaudeTimeoutSeconds = int.TryParse(configuration["CLAUDE_TIMEOUT_SECONDS"], out var claudeTimeout)
            ? Math.Clamp(claudeTimeout, 10, 300)
            : 60,
        ClaudeMaxRetries = int.TryParse(configuration["CLAUDE_MAX_RETRIES"], out var claudeRetries)
            ? Math.Clamp(claudeRetries, 0, 5)
            : 1,
        DatabasePath = configuration["DATABASE_PATH"] ?? "/app/data/tradingagent.db",
        RetentionDays = int.TryParse(configuration["RETENTION_DAYS"], out var retentionDays) ? Math.Max(retentionDays, 1) : 30,
        WebhookSecret = configuration["WEBHOOK_SECRET"] ?? string.Empty,
        MinConfidenceToNotify = int.TryParse(configuration["MIN_CONFIDENCE_TO_NOTIFY"], out var minConfidence)
            ? Math.Clamp(minConfidence, 0, 100)
            : 70,
        SendIgnoredSignals = bool.TryParse(configuration["SEND_IGNORED_SIGNALS"], out var sendIgnored) && sendIgnored,
        SendWaitSignals = bool.TryParse(configuration["SEND_WAIT_SIGNALS"], out var sendWait) && sendWait,
        PaperTradingEnabled = !bool.TryParse(configuration["PAPER_TRADING_ENABLED"], out var paperTrading) || paperTrading,
        DefaultPositionQuantity = decimal.TryParse(configuration["DEFAULT_POSITION_QUANTITY"], out var quantity) && quantity > 0
            ? quantity
            : 1,
        AllowTestTrades = bool.TryParse(configuration["ALLOW_TEST_TRADES"], out var allowTestTrades) && allowTestTrades,
        SendTestTelegram = bool.TryParse(configuration["SEND_TEST_TELEGRAM"], out var sendTestTelegram) && sendTestTelegram,
        MarketProvider = configuration["MARKET_PROVIDER"] ?? "NASDAQ",
        MarketTimezone = configuration["MARKET_TIMEZONE"] ?? "America/New_York",
        AllowPreMarket = bool.TryParse(configuration["ALLOW_PREMARKET"], out var allowPreMarket) && allowPreMarket,
        AllowAfterHours = bool.TryParse(configuration["ALLOW_AFTER_HOURS"], out var allowAfterHours) && allowAfterHours,
        AllowOvernight = bool.TryParse(configuration["ALLOW_OVERNIGHT"], out var allowOvernight) && allowOvernight,
        IgnoreSignalsWhenMarketClosed = !bool.TryParse(configuration["IGNORE_SIGNALS_WHEN_MARKET_CLOSED"], out var ignoreWhenClosed) || ignoreWhenClosed,
        SendMarketClosedNotifications = bool.TryParse(configuration["SEND_MARKET_CLOSED_NOTIFICATIONS"], out var sendMarketClosed) && sendMarketClosed,
        Enable24_5Trading = !bool.TryParse(configuration["ENABLE_24_5_TRADING"], out var enable24_5) || enable24_5,
        MinConfidenceRegular = int.TryParse(configuration["MIN_CONFIDENCE_REGULAR"], out var minConfRegular)
            ? Math.Clamp(minConfRegular, 0, 100)
            : 60,
        MinConfidencePremarket = int.TryParse(configuration["MIN_CONFIDENCE_PREMARKET"], out var minConfPremarket)
            ? Math.Clamp(minConfPremarket, 0, 100)
            : 70,
        MinConfidenceAfterHours = int.TryParse(configuration["MIN_CONFIDENCE_AFTER_HOURS"], out var minConfAfterHours)
            ? Math.Clamp(minConfAfterHours, 0, 100)
            : 70,
        MinConfidenceOvernight = int.TryParse(configuration["MIN_CONFIDENCE_OVERNIGHT"], out var minConfOvernight)
            ? Math.Clamp(minConfOvernight, 0, 100)
            : 75,
        MaxPriceDriftPercentRegular = decimal.TryParse(configuration["MAX_PRICE_DRIFT_PERCENT_REGULAR"], out var driftRegular)
            ? Math.Clamp(driftRegular, 0.1m, 10m)
            : 1.0m,
        MaxPriceDriftPercentExtended = decimal.TryParse(configuration["MAX_PRICE_DRIFT_PERCENT_EXTENDED"], out var driftExtended)
            ? Math.Clamp(driftExtended, 0.1m, 15m)
            : 2.5m,
        AllowScaleIn = bool.TryParse(configuration["ALLOW_SCALE_IN"], out var allowScaleIn) && allowScaleIn,
        MaxPositionsPerSymbol = int.TryParse(configuration["MAX_POSITIONS_PER_SYMBOL"], out var maxPositions) && maxPositions > 0
            ? maxPositions
            : 1,
        SendDuplicateBuyNotifications = bool.TryParse(configuration["SEND_DUPLICATE_BUY_NOTIFICATIONS"], out var sendDuplicateBuy) && sendDuplicateBuy
    };
});

builder.Services.AddDbContext<TradingAgentDbContext>((serviceProvider, options) =>
{
    var settings = serviceProvider.GetRequiredService<AppSettings>();
    var databaseDirectory = Path.GetDirectoryName(settings.DatabasePath);

    if (!string.IsNullOrWhiteSpace(databaseDirectory))
    {
        Directory.CreateDirectory(databaseDirectory);
    }

    options.UseSqlite($"Data Source={settings.DatabasePath}");
});

builder.Services.AddHttpClient(TelegramNotificationService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient(ClaudeAnalysisService.HttpClientName, (serviceProvider, client) =>
{
    var appSettings = serviceProvider.GetRequiredService<AppSettings>();
    client.BaseAddress = new Uri("https://api.anthropic.com/");
    client.Timeout = TimeSpan.FromSeconds(Math.Max(appSettings.ClaudeTimeoutSeconds, 10));
});

builder.Services.AddHttpClient(YahooFinanceService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TradingAgent/1.0");
});

builder.Services.AddScoped<IClaudeAnalysisService, ClaudeAnalysisService>();
builder.Services.AddScoped<ITelegramNotificationService, TelegramNotificationService>();
builder.Services.AddScoped<IYahooFinanceService, YahooFinanceService>();
builder.Services.AddScoped<IPositionManagerService, PositionManagerService>();
builder.Services.AddScoped<IWebhookProcessorService, WebhookProcessorService>();
builder.Services.AddSingleton<IRuntimeSettingsService, RuntimeSettingsService>();
builder.Services.AddSingleton<IMarketCalendarService, MarketCalendarService>();
builder.Services.AddSingleton<IMarketProvider, NasdaqMarketProvider>();
builder.Services.AddSingleton<IMarketProvider, NyseMarketProvider>();
builder.Services.AddSingleton<IMarketProvider, CryptoMarketProvider>();
builder.Services.AddSingleton<IMarketProvider, ForexMarketProvider>();
builder.Services.AddScoped<IMarketStatusService, MarketStatusService>();
builder.Services.AddHostedService<SignalRetentionCleanupService>();

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalException");
        logger.LogError("Unhandled exception while processing a request.");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected server error occurred." });
    });
});

app.UseDefaultFiles();
app.UseStaticFiles();

await EnsureDatabaseAsync(app);
await ApplyRuntimeSettingsAsync(app);
ValidateRequiredConfiguration(app.Services);

var api = app.MapGroup("/api");

api.MapPost("/tradingview/webhook", HandleTradingViewWebhookAsync)
    .WithName("ReceiveTradingViewWebhook");

api.MapGet("/signals", GetSignalsAsync)
    .WithName("GetSignals");

api.MapGet("/signals/{id:int}", GetSignalByIdAsync)
    .WithName("GetSignalById");

api.MapPatch("/signals/{id:int}", PatchSignalAsync)
    .WithName("PatchSignal");

api.MapDelete("/signals/{id:int}", DeleteSignalAsync)
    .WithName("DeleteSignal");

api.MapGet("/test-claude", TestClaudeAsync)
    .WithName("TestClaude");

api.MapGet("/test-telegram", TestTelegramAsync)
    .WithName("TestTelegram");

api.MapGet("/test-yahoo", TestYahooAsync)
    .WithName("TestYahoo");

api.MapGet("/settings", GetSettingsAsync)
    .WithName("GetSettings");

api.MapPatch("/settings", PatchSettingsAsync)
    .WithName("PatchSettings");

api.MapGet("/positions/open", GetOpenPositionsAsync)
    .WithName("GetOpenPositions");

api.MapGet("/positions/closed", GetClosedPositionsAsync)
    .WithName("GetClosedPositions");

api.MapGet("/positions", GetPositionsAsync)
    .WithName("GetPositions");

api.MapPost("/positions/{id:int}/close", ClosePositionAsync)
    .WithName("ClosePosition");

api.MapPatch("/positions/{id:int}", PatchPositionAsync)
    .WithName("PatchPosition");

api.MapPost("/test-webhook", HandleTestWebhookAsync)
    .WithName("TestWebhook");

api.MapGet("/webhooks/history", GetWebhookHistoryAsync)
    .WithName("GetWebhookHistory");

api.MapGet("/webhooks/history/{id:int}", GetWebhookHistoryByIdAsync)
    .WithName("GetWebhookHistoryById");

api.MapDelete("/webhooks/history", DeleteWebhookHistoryAsync)
    .WithName("DeleteWebhookHistory");

api.MapGet("/market/status", GetMarketStatusAsync)
    .WithName("GetMarketStatus");

api.MapGet("/market/status/{market}", GetMarketStatusByNameAsync)
    .WithName("GetMarketStatusByName");

api.MapGet("/market/calendar", GetMarketCalendarAsync)
    .WithName("GetMarketCalendar");

api.MapGet("/test-market", TestMarketAsync)
    .WithName("TestMarket");

app.MapFallbackToFile("index.html");

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<TradingAgentDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    await EnsureSignalMarketDataTableAsync(dbContext);
    await EnsureTradingSignalColumnsAsync(dbContext);
    await EnsurePositionsTableAsync(dbContext);
    await EnsurePositionColumnsAsync(dbContext);
    await EnsureWebhookRequestLogsTableAsync(dbContext);
    await EnsureTradingSignalAuditColumnsAsync(dbContext);
    await EnsureTradingSignalMarketColumnsAsync(dbContext);
    await EnsureTradingSignalReasonCategoryColumnAsync(dbContext);
    await EnsureRuntimeSettingsTableAsync(dbContext);
}

static async Task EnsureTradingSignalReasonCategoryColumnAsync(TradingAgentDbContext dbContext)
{
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "PRAGMA table_info(TradingSignals);";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            existingColumns.Add(reader.GetString(1));
        }
    }

    if (!existingColumns.Contains("ReasonCategories"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE TradingSignals ADD COLUMN ReasonCategories TEXT NULL;");
    }
}

static async Task ApplyRuntimeSettingsAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var runtimeSettings = scope.ServiceProvider.GetRequiredService<IRuntimeSettingsService>();
    await runtimeSettings.ApplyDatabaseOverridesAsync();
}

static async Task EnsureRuntimeSettingsTableAsync(TradingAgentDbContext dbContext)
{
    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS RuntimeSettings (
            Key TEXT NOT NULL CONSTRAINT PK_RuntimeSettings PRIMARY KEY,
            Value TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL
        );
        """);
}

static async Task EnsureSignalMarketDataTableAsync(TradingAgentDbContext dbContext)
{
    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS SignalMarketData (
            Id INTEGER NOT NULL CONSTRAINT PK_SignalMarketData PRIMARY KEY AUTOINCREMENT,
            TradingSignalId INTEGER NOT NULL,
            Symbol TEXT NOT NULL,
            CurrentPrice REAL NULL,
            Ema9 REAL NULL,
            Ema20 REAL NULL,
            Ema50 REAL NULL,
            Rsi14 REAL NULL,
            Macd REAL NULL,
            MacdSignal REAL NULL,
            Atr REAL NULL,
            CurrentVolume INTEGER NULL,
            AverageVolume20 INTEGER NULL,
            Week52High REAL NULL,
            Week52Low REAL NULL,
            FetchedAtUtc TEXT NOT NULL,
            CONSTRAINT FK_SignalMarketData_TradingSignals_TradingSignalId
                FOREIGN KEY (TradingSignalId) REFERENCES TradingSignals (Id) ON DELETE CASCADE
        );
        """);

    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE UNIQUE INDEX IF NOT EXISTS IX_SignalMarketData_TradingSignalId
            ON SignalMarketData (TradingSignalId);
        """);
}

static async Task EnsureTradingSignalColumnsAsync(TradingAgentDbContext dbContext)
{
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "PRAGMA table_info(TradingSignals);";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            existingColumns.Add(reader.GetString(1));
        }
    }

    var migrations = new Dictionary<string, string>
    {
        ["RiskRewardRatio"] = "ALTER TABLE TradingSignals ADD COLUMN RiskRewardRatio REAL NULL;",
        ["PositionSizePercent"] = "ALTER TABLE TradingSignals ADD COLUMN PositionSizePercent REAL NULL;",
        ["ShouldNotify"] = "ALTER TABLE TradingSignals ADD COLUMN ShouldNotify INTEGER NULL;",
        ["Notified"] = "ALTER TABLE TradingSignals ADD COLUMN Notified INTEGER NOT NULL DEFAULT 0;"
    };

    foreach (var (column, sql) in migrations)
    {
        if (!existingColumns.Contains(column))
        {
            await dbContext.Database.ExecuteSqlRawAsync(sql);
        }
    }
}

static async Task EnsurePositionsTableAsync(TradingAgentDbContext dbContext)
{
    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS Positions (
            Id INTEGER NOT NULL CONSTRAINT PK_Positions PRIMARY KEY AUTOINCREMENT,
            Symbol TEXT NOT NULL,
            Status TEXT NOT NULL,
            EntrySignalId INTEGER NOT NULL,
            ExitSignalId INTEGER NULL,
            EntryPrice REAL NOT NULL,
            ExitPrice REAL NULL,
            Quantity REAL NOT NULL,
            EntryTimeUtc TEXT NOT NULL,
            ExitTimeUtc TEXT NULL,
            ProfitLoss REAL NULL,
            ProfitLossPercent REAL NULL,
            MaxRiskPercent REAL NULL,
            Notes TEXT NULL
        );
        """);

    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS IX_Positions_Symbol ON Positions (Symbol);
        """);

    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS IX_Positions_Status ON Positions (Status);
        """);

    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS IX_Positions_EntryTimeUtc ON Positions (EntryTimeUtc);
        """);
}

static async Task EnsurePositionColumnsAsync(TradingAgentDbContext dbContext)
{
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "PRAGMA table_info(Positions);";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            existingColumns.Add(reader.GetString(1));
        }
    }

    if (!existingColumns.Contains("EntryMarketSession"))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE Positions ADD COLUMN EntryMarketSession TEXT NULL;");
    }
}

static async Task EnsureWebhookRequestLogsTableAsync(TradingAgentDbContext dbContext)
{
    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS WebhookRequestLogs (
            Id INTEGER NOT NULL CONSTRAINT PK_WebhookRequestLogs PRIMARY KEY AUTOINCREMENT,
            ReceivedAtUtc TEXT NOT NULL,
            Source TEXT NOT NULL,
            RemoteIp TEXT NULL,
            UserAgent TEXT NULL,
            RawPayload TEXT NOT NULL,
            HeadersJson TEXT NULL,
            IsTest INTEGER NOT NULL DEFAULT 0,
            TradingSignalId INTEGER NULL,
            ResultStatus TEXT NOT NULL,
            ErrorMessage TEXT NULL
        );
        """);

    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS IX_WebhookRequestLogs_ReceivedAtUtc ON WebhookRequestLogs (ReceivedAtUtc);
        """);

    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS IX_WebhookRequestLogs_IsTest ON WebhookRequestLogs (IsTest);
        """);
}

static async Task EnsureTradingSignalAuditColumnsAsync(TradingAgentDbContext dbContext)
{
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "PRAGMA table_info(TradingSignals);";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            existingColumns.Add(reader.GetString(1));
        }
    }

    var migrations = new Dictionary<string, string>
    {
        ["IsTest"] = "ALTER TABLE TradingSignals ADD COLUMN IsTest INTEGER NOT NULL DEFAULT 0;",
        ["Source"] = "ALTER TABLE TradingSignals ADD COLUMN Source TEXT NOT NULL DEFAULT 'UNKNOWN';"
    };

    foreach (var (column, sql) in migrations)
    {
        if (!existingColumns.Contains(column))
        {
            await dbContext.Database.ExecuteSqlRawAsync(sql);
        }
    }
}

static async Task EnsureTradingSignalMarketColumnsAsync(TradingAgentDbContext dbContext)
{
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "PRAGMA table_info(TradingSignals);";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            existingColumns.Add(reader.GetString(1));
        }
    }

    var migrations = new Dictionary<string, string>
    {
        ["IgnoredReason"] = "ALTER TABLE TradingSignals ADD COLUMN IgnoredReason TEXT NULL;",
        ["IgnoredBy"] = "ALTER TABLE TradingSignals ADD COLUMN IgnoredBy TEXT NULL;",
        ["MarketStatus"] = "ALTER TABLE TradingSignals ADD COLUMN MarketStatus TEXT NULL;",
        ["MarketName"] = "ALTER TABLE TradingSignals ADD COLUMN MarketName TEXT NULL;",
        ["MarketCheckedAtUtc"] = "ALTER TABLE TradingSignals ADD COLUMN MarketCheckedAtUtc TEXT NULL;",
        ["MarketSession"] = "ALTER TABLE TradingSignals ADD COLUMN MarketSession TEXT NULL;"
    };

    foreach (var (column, sql) in migrations)
    {
        if (!existingColumns.Contains(column))
        {
            await dbContext.Database.ExecuteSqlRawAsync(sql);
        }
    }
}

static void ValidateRequiredConfiguration(IServiceProvider services)
{
    var settings = services.GetRequiredService<AppSettings>();
    if (string.IsNullOrWhiteSpace(settings.WebhookSecret))
    {
        throw new InvalidOperationException("WEBHOOK_SECRET must be configured before starting TradingAgent.");
    }
}

static string ResolveClaudeApiKey(IConfiguration configuration)
{
    var claudeApiKey = configuration["CLAUDE_API_KEY"];
    if (!string.IsNullOrWhiteSpace(claudeApiKey))
    {
        return claudeApiKey;
    }

    return configuration["ANTHROPIC_API_KEY"] ?? string.Empty;
}

static async Task<IResult> HandleTradingViewWebhookAsync(
    HttpRequest request,
    IWebhookProcessorService webhookProcessorService)
{
    using var reader = new StreamReader(request.Body);
    var rawPayload = await reader.ReadToEndAsync(CancellationToken.None);
    var result = await webhookProcessorService.ProcessAsync(request, rawPayload, forceTest: false);
    return ToWebhookResult(result);
}

static async Task<IResult> HandleTestWebhookAsync(
    HttpRequest request,
    TestWebhookRequest testRequest,
    AppSettings settings,
    IWebhookProcessorService webhookProcessorService)
{
    if (string.IsNullOrWhiteSpace(testRequest.Symbol) || string.IsNullOrWhiteSpace(testRequest.Signal))
    {
        return TypedResults.BadRequest(new WebhookProcessResponse
        {
            Success = false,
            ResultStatus = "BAD_REQUEST",
            Error = "Symbol and signal are required."
        });
    }

    var payload = JsonSerializer.Serialize(new
    {
        symbol = testRequest.Symbol.Trim().ToUpperInvariant(),
        signal = testRequest.Signal.Trim().ToUpperInvariant(),
        price = testRequest.Price ?? "100.00",
        timeframe = testRequest.Timeframe ?? "1H",
        strategy = testRequest.Strategy ?? "TEST",
        source = WebhookSources.CursorTest,
        secret = settings.WebhookSecret
    });

    var result = await webhookProcessorService.ProcessAsync(request, payload, forceTest: true);
    return ToWebhookResult(result);
}

static IResult ToWebhookResult(WebhookProcessResponse result)
    => result.ResultStatus switch
    {
        WebhookResultStatuses.Unauthorized => TypedResults.Json(result, statusCode: StatusCodes.Status401Unauthorized),
        WebhookResultStatuses.BadRequest => TypedResults.Json(result, statusCode: StatusCodes.Status400BadRequest),
        WebhookResultStatuses.Error => TypedResults.Json(result, statusCode: StatusCodes.Status500InternalServerError),
        _ => TypedResults.Json(result, statusCode: StatusCodes.Status200OK)
    };

static async Task<IResult> GetWebhookHistoryAsync(
    string? filter,
    string? source,
    string? status,
    string? symbol,
    string? from,
    string? to,
    int? page,
    int? pageSize,
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var query = dbContext.WebhookRequestLogs.AsNoTracking();

    if (string.Equals(filter, "real", StringComparison.OrdinalIgnoreCase))
    {
        query = query.Where(log => !log.IsTest);
    }
    else if (string.Equals(filter, "test", StringComparison.OrdinalIgnoreCase))
    {
        query = query.Where(log => log.IsTest);
    }

    if (!string.IsNullOrWhiteSpace(source))
    {
        var normalizedSource = source.Trim().ToUpperInvariant();
        query = query.Where(log => log.Source.ToUpper() == normalizedSource);
    }

    if (!string.IsNullOrWhiteSpace(status))
    {
        var normalizedStatus = status.Trim().ToUpperInvariant();
        query = query.Where(log => log.ResultStatus.ToUpper() == normalizedStatus);
    }

    var fromUtc = PaginationHelper.ParseUtcDate(from);
    if (fromUtc.HasValue)
    {
        query = query.Where(log => log.ReceivedAtUtc >= fromUtc.Value);
    }

    var toUtc = PaginationHelper.ParseUtcDate(to);
    if (toUtc.HasValue)
    {
        query = query.Where(log => log.ReceivedAtUtc <= toUtc.Value);
    }

    if (!string.IsNullOrWhiteSpace(symbol))
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var matchingSignalIds = dbContext.TradingSignals
            .AsNoTracking()
            .Where(signal => signal.Symbol == normalizedSymbol)
            .Select(signal => signal.Id);

        query = query.Where(log =>
            log.TradingSignalId.HasValue && matchingSignalIds.Contains(log.TradingSignalId.Value));
    }

    query = query.OrderByDescending(log => log.ReceivedAtUtc);

    List<WebhookRequestLog> logs;
    int total = 0;
    int resolvedPage = 1;
    int resolvedPageSize = 25;

    if (PaginationHelper.WantsPagination(page, pageSize))
    {
        (resolvedPage, resolvedPageSize) = PaginationHelper.Resolve(page, pageSize);
        total = await query.CountAsync(cancellationToken);
        logs = await query
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(cancellationToken);
    }
    else
    {
        logs = await query.Take(200).ToListAsync(cancellationToken);
    }

    var signalIds = logs
        .Where(log => log.TradingSignalId.HasValue)
        .Select(log => log.TradingSignalId!.Value)
        .Distinct()
        .ToList();

    var signals = await dbContext.TradingSignals
        .AsNoTracking()
        .Where(signal => signalIds.Contains(signal.Id))
        .ToDictionaryAsync(signal => signal.Id, cancellationToken);

    var response = logs.Select(log =>
    {
        signals.TryGetValue(log.TradingSignalId ?? 0, out var signal);
        return (object)new
        {
            log.Id,
            log.ReceivedAtUtc,
            log.Source,
            log.IsTest,
            log.ResultStatus,
            log.ErrorMessage,
            log.TradingSignalId,
            symbol = signal?.Symbol,
            signalType = signal?.OriginalSignal,
            claudeDecision = signal?.ClaudeAction
        };
    }).ToList();

    if (PaginationHelper.WantsPagination(page, pageSize))
    {
        return TypedResults.Ok(PaginationHelper.Create(response, resolvedPage, resolvedPageSize, total));
    }

    return TypedResults.Ok(response);
}

static async Task<Results<Ok<WebhookRequestLog>, NotFound>> GetWebhookHistoryByIdAsync(
    int id,
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var log = await dbContext.WebhookRequestLogs.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    return log is null ? TypedResults.NotFound() : TypedResults.Ok(log);
}

static async Task<Ok<object>> DeleteWebhookHistoryAsync(
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var deleted = await dbContext.WebhookRequestLogs.ExecuteDeleteAsync(cancellationToken);
    return TypedResults.Ok((object)new { deleted });
}

static async Task<IResult> GetSignalsAsync(
    string? symbol,
    string? signal,
    string? decision,
    string? session,
    bool? notified,
    string? from,
    string? to,
    bool? isTest,
    int? page,
    int? pageSize,
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var query = dbContext.TradingSignals.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(symbol))
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        query = query.Where(item => item.Symbol == normalizedSymbol);
    }

    if (!string.IsNullOrWhiteSpace(signal))
    {
        var normalizedSignal = signal.Trim().ToUpperInvariant();
        query = query.Where(item => item.OriginalSignal == normalizedSignal);
    }

    if (!string.IsNullOrWhiteSpace(decision))
    {
        var normalizedDecision = decision.Trim().ToUpperInvariant();
        query = query.Where(item => item.ClaudeAction != null && item.ClaudeAction.ToUpper() == normalizedDecision);
    }

    if (!string.IsNullOrWhiteSpace(session))
    {
        var normalizedSession = session.Trim().ToUpperInvariant();
        query = query.Where(item =>
            (item.MarketSession != null && item.MarketSession.ToUpper() == normalizedSession)
            || (item.MarketStatus != null && item.MarketStatus.ToUpper() == normalizedSession));
    }

    if (notified.HasValue)
    {
        query = query.Where(item => item.Notified == notified.Value);
    }

    if (isTest.HasValue)
    {
        query = query.Where(item => item.IsTest == isTest.Value);
    }

    var fromUtc = PaginationHelper.ParseUtcDate(from);
    if (fromUtc.HasValue)
    {
        query = query.Where(item => item.CreatedAtUtc >= fromUtc.Value);
    }

    var toUtc = PaginationHelper.ParseUtcDate(to);
    if (toUtc.HasValue)
    {
        query = query.Where(item => item.CreatedAtUtc <= toUtc.Value);
    }

    query = query.OrderByDescending(item => item.CreatedAtUtc);

    if (!PaginationHelper.WantsPagination(page, pageSize))
    {
        var signals = await query
            .Include(item => item.MarketData)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(signals);
    }

    var (resolvedPage, resolvedPageSize) = PaginationHelper.Resolve(page, pageSize);
    var total = await query.CountAsync(cancellationToken);
    var items = await query
        .Include(item => item.MarketData)
        .Skip((resolvedPage - 1) * resolvedPageSize)
        .Take(resolvedPageSize)
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(PaginationHelper.Create(items, resolvedPage, resolvedPageSize, total));
}

static async Task<Results<Ok<TradingSignal>, NotFound>> GetSignalByIdAsync(
    int id,
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var signal = await dbContext.TradingSignals
        .AsNoTracking()
        .Include(item => item.MarketData)
        .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    return signal is null ? TypedResults.NotFound() : TypedResults.Ok(signal);
}

static async Task<Results<Ok<TradingSignal>, NotFound>> PatchSignalAsync(
    int id,
    UpdateTradingSignalRequest request,
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var signal = await dbContext.TradingSignals.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (signal is null)
    {
        return TypedResults.NotFound();
    }

    if (request.ProfitLoss.HasValue)
    {
        signal.ProfitLoss = request.ProfitLoss.Value;
    }

    if (request.Notes is not null)
    {
        signal.Notes = request.Notes.Trim();
    }

    if (request.IsClosed.HasValue)
    {
        signal.IsClosed = request.IsClosed.Value;
    }

    try
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
        return TypedResults.NotFound();
    }

    return TypedResults.Ok(signal);
}

static async Task<Results<NoContent, NotFound>> DeleteSignalAsync(
    int id,
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var signal = await dbContext.TradingSignals.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (signal is null)
    {
        return TypedResults.NotFound();
    }

    dbContext.TradingSignals.Remove(signal);
    try
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
        return TypedResults.NotFound();
    }

    return TypedResults.NoContent();
}

static async Task<IResult> TestClaudeAsync(
    IClaudeAnalysisService claudeAnalysisService,
    CancellationToken cancellationToken)
{
    var result = await claudeAnalysisService.TestAsync(cancellationToken);

    return TypedResults.Ok(new
    {
        success = result.Success,
        httpStatus = result.HttpStatusCode,
        model = result.Model,
        rawResponse = result.RawResponse,
        parsedResponse = result.ParsedResponse,
        elapsedMs = result.ElapsedMilliseconds,
        error = result.Error
    });
}

static async Task<IResult> TestTelegramAsync(
    ITelegramNotificationService telegramNotificationService,
    CancellationToken cancellationToken)
{
    var result = await telegramNotificationService.SendTestAsync(cancellationToken);

    return TypedResults.Ok(new
    {
        success = result.Success,
        httpStatus = result.HttpStatusCode,
        error = result.Error
    });
}

static async Task<IResult> TestYahooAsync(
    IYahooFinanceService yahooFinanceService,
    CancellationToken cancellationToken)
{
    var marketContext = await yahooFinanceService.FetchMarketContextAsync("NVDA", cancellationToken);
    return TypedResults.Ok(marketContext);
}

static IResult GetSettingsAsync(IRuntimeSettingsService runtimeSettings)
{
    var response = runtimeSettings.GetSettings();
    return TypedResults.Ok(new
    {
        settings = response.Values,
        overriddenKeys = response.OverriddenKeys,
        editableKeys = response.EditableKeys
    });
}

static async Task<IResult> PatchSettingsAsync(
    UpdateSettingsRequest request,
    IRuntimeSettingsService runtimeSettings,
    CancellationToken cancellationToken)
{
    if (request.Settings is null || request.Settings.Count == 0)
    {
        return TypedResults.BadRequest(new { error = "At least one setting must be provided." });
    }

    var updates = request.Settings.ToDictionary(
        pair => pair.Key,
        pair => pair.Value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => pair.Value.GetString() ?? string.Empty,
            System.Text.Json.JsonValueKind.True => "true",
            System.Text.Json.JsonValueKind.False => "false",
            System.Text.Json.JsonValueKind.Number => pair.Value.GetRawText(),
            _ => pair.Value.GetRawText()
        },
        StringComparer.OrdinalIgnoreCase);

    try
    {
        var response = await runtimeSettings.UpdateSettingsAsync(updates, cancellationToken);
        return TypedResults.Ok(new
        {
            settings = response.Values,
            overriddenKeys = response.OverriddenKeys,
            editableKeys = response.EditableKeys
        });
    }
    catch (ArgumentException exception)
    {
        return TypedResults.BadRequest(new { error = exception.Message });
    }
}

static IResult GetMarketStatusAsync(IMarketStatusService marketStatusService)
    => TypedResults.Ok(marketStatusService.GetStatus());

static IResult GetMarketStatusByNameAsync(string market, IMarketStatusService marketStatusService)
    => TypedResults.Ok(marketStatusService.GetStatus(market));

static IResult GetMarketCalendarAsync(IMarketStatusService marketStatusService, int? year)
    => TypedResults.Ok(marketStatusService.GetCalendar(year: year));

static IResult TestMarketAsync(
    IMarketStatusService marketStatusService,
    string? datetimeUtc,
    string? market)
{
    DateTime? atUtc = null;
    if (!string.IsNullOrWhiteSpace(datetimeUtc)
        && DateTime.TryParse(datetimeUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
    {
        atUtc = parsed.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : parsed.ToUniversalTime();
    }

    var status = marketStatusService.GetStatus(market, atUtc);
    return TypedResults.Ok(status);
}

static async Task<IResult> GetPositionsAsync(
    string? symbol,
    string? status,
    string? outcome,
    string? from,
    string? to,
    int? page,
    int? pageSize,
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var query = dbContext.Positions.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(symbol))
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        query = query.Where(position => position.Symbol == normalizedSymbol);
    }

    if (!string.IsNullOrWhiteSpace(status))
    {
        var normalizedStatus = status.Trim().ToUpperInvariant();
        if (normalizedStatus is "OPEN" or "CLOSED")
        {
            query = query.Where(position => position.Status == normalizedStatus);
        }
    }

    if (string.Equals(outcome, "win", StringComparison.OrdinalIgnoreCase))
    {
        query = query.Where(position => position.ProfitLoss.HasValue && position.ProfitLoss > 0);
    }
    else if (string.Equals(outcome, "loss", StringComparison.OrdinalIgnoreCase))
    {
        query = query.Where(position => position.ProfitLoss.HasValue && position.ProfitLoss < 0);
    }

    var fromUtc = PaginationHelper.ParseUtcDate(from);
    if (fromUtc.HasValue)
    {
        query = query.Where(position =>
            position.EntryTimeUtc >= fromUtc.Value
            || (position.ExitTimeUtc.HasValue && position.ExitTimeUtc.Value >= fromUtc.Value));
    }

    var toUtc = PaginationHelper.ParseUtcDate(to);
    if (toUtc.HasValue)
    {
        query = query.Where(position => position.EntryTimeUtc <= toUtc.Value);
    }

    query = query.OrderByDescending(position => position.ExitTimeUtc ?? position.EntryTimeUtc);

    if (!PaginationHelper.WantsPagination(page, pageSize))
    {
        var positions = await query.ToListAsync(cancellationToken);
        return TypedResults.Ok(positions);
    }

    var (resolvedPage, resolvedPageSize) = PaginationHelper.Resolve(page, pageSize);
    var total = await query.CountAsync(cancellationToken);
    var pageItems = await query
        .Skip((resolvedPage - 1) * resolvedPageSize)
        .Take(resolvedPageSize)
        .ToListAsync(cancellationToken);

    var items = await ToPositionListItemsAsync(pageItems, dbContext, cancellationToken);
    return TypedResults.Ok(PaginationHelper.Create(items, resolvedPage, resolvedPageSize, total));
}

static async Task<List<PositionListItem>> ToPositionListItemsAsync(
    List<Position> positions,
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    if (positions.Count == 0)
    {
        return [];
    }

    var signalIds = positions.Select(position => position.EntrySignalId).Distinct().ToList();
    var signals = await dbContext.TradingSignals
        .AsNoTracking()
        .Where(signal => signalIds.Contains(signal.Id))
        .ToDictionaryAsync(signal => signal.Id, cancellationToken);

    return positions.Select(position =>
    {
        signals.TryGetValue(position.EntrySignalId, out var signal);
        return new PositionListItem
        {
            Id = position.Id,
            Symbol = position.Symbol,
            Status = position.Status,
            EntrySignalId = position.EntrySignalId,
            ExitSignalId = position.ExitSignalId,
            EntryPrice = position.EntryPrice,
            ExitPrice = position.ExitPrice,
            Quantity = position.Quantity,
            EntryTimeUtc = position.EntryTimeUtc,
            ExitTimeUtc = position.ExitTimeUtc,
            ProfitLoss = position.ProfitLoss,
            ProfitLossPercent = position.ProfitLossPercent,
            MaxRiskPercent = position.MaxRiskPercent,
            EntryMarketSession = position.EntryMarketSession,
            Notes = position.Notes,
            StopLoss = signal?.SuggestedStopLoss,
            TakeProfit = signal?.SuggestedTakeProfit
        };
    }).ToList();
}

static async Task<Ok<List<Position>>> GetOpenPositionsAsync(
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var positions = await dbContext.Positions
        .AsNoTracking()
        .Where(position => position.Status == PositionStatus.Open)
        .OrderByDescending(position => position.EntryTimeUtc)
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(positions);
}

static async Task<Ok<List<Position>>> GetClosedPositionsAsync(
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var positions = await dbContext.Positions
        .AsNoTracking()
        .Where(position => position.Status == PositionStatus.Closed)
        .OrderByDescending(position => position.ExitTimeUtc)
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(positions);
}

static async Task<Results<Ok<Position>, NotFound>> ClosePositionAsync(
    int id,
    ClosePositionRequest request,
    IPositionManagerService positionManagerService,
    ITelegramNotificationService telegramNotificationService,
    CancellationToken cancellationToken)
{
    var position = await positionManagerService.ClosePositionAsync(id, request.ExitPrice, null, cancellationToken);
    if (position is null)
    {
        return TypedResults.NotFound();
    }

    await telegramNotificationService.SendPositionClosedAsync(position, cancellationToken);
    return TypedResults.Ok(position);
}

static async Task<Results<Ok<Position>, NotFound>> PatchPositionAsync(
    int id,
    UpdatePositionRequest request,
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var position = await dbContext.Positions.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (position is null)
    {
        return TypedResults.NotFound();
    }

    if (request.Notes is not null)
    {
        position.Notes = request.Notes.Trim();
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return TypedResults.Ok(position);
}
