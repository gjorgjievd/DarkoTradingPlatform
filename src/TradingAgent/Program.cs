using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TradingAgent.BackgroundServices;
using TradingAgent.Configuration;
using TradingAgent.Data;
using TradingAgent.DTOs;
using TradingAgent.Models;
using TradingAgent.Services;

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
        DatabasePath = configuration["DATABASE_PATH"] ?? "/app/data/tradingagent.db",
        RetentionDays = int.TryParse(configuration["RETENTION_DAYS"], out var retentionDays) ? Math.Max(retentionDays, 1) : 30,
        WebhookSecret = configuration["WEBHOOK_SECRET"] ?? string.Empty,
        MinConfidenceToNotify = int.TryParse(configuration["MIN_CONFIDENCE_TO_NOTIFY"], out var minConfidence)
            ? Math.Clamp(minConfidence, 0, 100)
            : 70,
        SendIgnoredSignals = bool.TryParse(configuration["SEND_IGNORED_SIGNALS"], out var sendIgnored) && sendIgnored,
        PaperTradingEnabled = !bool.TryParse(configuration["PAPER_TRADING_ENABLED"], out var paperTrading) || paperTrading,
        DefaultPositionQuantity = decimal.TryParse(configuration["DEFAULT_POSITION_QUANTITY"], out var quantity) && quantity > 0
            ? quantity
            : 1,
        AllowTestTrades = bool.TryParse(configuration["ALLOW_TEST_TRADES"], out var allowTestTrades) && allowTestTrades,
        SendTestTelegram = bool.TryParse(configuration["SEND_TEST_TELEGRAM"], out var sendTestTelegram) && sendTestTelegram
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

builder.Services.AddHttpClient(ClaudeAnalysisService.HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
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
    await EnsureWebhookRequestLogsTableAsync(dbContext);
    await EnsureTradingSignalAuditColumnsAsync(dbContext);
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
    IWebhookProcessorService webhookProcessorService,
    CancellationToken cancellationToken)
{
    using var reader = new StreamReader(request.Body);
    var rawPayload = await reader.ReadToEndAsync(cancellationToken);
    var result = await webhookProcessorService.ProcessAsync(request, rawPayload, forceTest: false, cancellationToken);
    return ToWebhookResult(result);
}

static async Task<IResult> HandleTestWebhookAsync(
    HttpRequest request,
    TestWebhookRequest testRequest,
    AppSettings settings,
    IWebhookProcessorService webhookProcessorService,
    CancellationToken cancellationToken)
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

    var result = await webhookProcessorService.ProcessAsync(request, payload, forceTest: true, cancellationToken);
    return ToWebhookResult(result);
}

static IResult ToWebhookResult(WebhookProcessResponse result)
    => result.ResultStatus switch
    {
        "UNAUTHORIZED" => TypedResults.Json(result, statusCode: StatusCodes.Status401Unauthorized),
        "BAD_REQUEST" => TypedResults.Json(result, statusCode: StatusCodes.Status400BadRequest),
        "ERROR" => TypedResults.Json(result, statusCode: StatusCodes.Status500InternalServerError),
        _ => TypedResults.Json(result, statusCode: StatusCodes.Status200OK)
    };

static async Task<Ok<List<object>>> GetWebhookHistoryAsync(
    string? filter,
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

    var logs = await query
        .OrderByDescending(log => log.ReceivedAtUtc)
        .Take(200)
        .ToListAsync(cancellationToken);

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

static async Task<Ok<List<TradingSignal>>> GetSignalsAsync(
    string? symbol,
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var query = dbContext.TradingSignals.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(symbol))
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        query = query.Where(signal => signal.Symbol == normalizedSymbol);
    }

    var signals = await query
        .Include(signal => signal.MarketData)
        .OrderByDescending(signal => signal.CreatedAtUtc)
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(signals);
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

static IResult GetSettingsAsync(AppSettings settings)
    => TypedResults.Ok(new Dictionary<string, object?>
    {
        ["MIN_CONFIDENCE_TO_NOTIFY"] = settings.MinConfidenceToNotify,
        ["SEND_IGNORED_SIGNALS"] = settings.SendIgnoredSignals,
        ["CLAUDE_ENABLED"] = settings.ClaudeEnabled,
        ["CLAUDE_MODEL"] = settings.ClaudeModel,
        ["PAPER_TRADING_ENABLED"] = settings.PaperTradingEnabled,
        ["DEFAULT_POSITION_QUANTITY"] = settings.DefaultPositionQuantity,
        ["ALLOW_TEST_TRADES"] = settings.AllowTestTrades,
        ["SEND_TEST_TELEGRAM"] = settings.SendTestTelegram
    });

static async Task<Ok<List<Position>>> GetPositionsAsync(
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var positions = await dbContext.Positions
        .AsNoTracking()
        .OrderByDescending(position => position.EntryTimeUtc)
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(positions);
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
