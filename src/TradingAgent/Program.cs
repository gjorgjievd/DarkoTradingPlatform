using System.Globalization;
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
        WebhookSecret = configuration["WEBHOOK_SECRET"] ?? string.Empty
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

builder.Services.AddScoped<IClaudeAnalysisService, ClaudeAnalysisService>();
builder.Services.AddScoped<ITelegramNotificationService, TelegramNotificationService>();
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

app.MapFallbackToFile("index.html");

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<TradingAgentDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
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
    TradingAgentDbContext dbContext,
    IClaudeAnalysisService claudeAnalysisService,
    ITelegramNotificationService telegramNotificationService,
    AppSettings settings,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken)
{
    var logger = loggerFactory.CreateLogger("TradingViewWebhook");

    using var reader = new StreamReader(request.Body);
    var rawPayload = await reader.ReadToEndAsync(cancellationToken);

    if (string.IsNullOrWhiteSpace(rawPayload))
    {
        return TypedResults.BadRequest(new { error = "Request body is required." });
    }

    TradingViewWebhookRequest? payload;
    try
    {
        payload = JsonSerializer.Deserialize<TradingViewWebhookRequest>(rawPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    catch (JsonException)
    {
        return TypedResults.BadRequest(new { error = "Invalid JSON payload." });
    }

    if (payload is null || string.IsNullOrWhiteSpace(payload.Symbol) || string.IsNullOrWhiteSpace(payload.Signal))
    {
        return TypedResults.BadRequest(new { error = "Payload must include symbol and signal." });
    }

    var headerSecret = request.Headers["X-Webhook-Secret"].FirstOrDefault()
        ?? request.Headers["X-TradingView-Secret"].FirstOrDefault();

    var providedSecret = string.IsNullOrWhiteSpace(payload.Secret) ? headerSecret : payload.Secret;
    if (!string.Equals(providedSecret, settings.WebhookSecret, StringComparison.Ordinal))
    {
        logger.LogWarning("Rejected webhook for symbol {Symbol} because of an invalid secret.", payload.Symbol);
        return TypedResults.Unauthorized();
    }

    logger.LogInformation(
        "Webhook received. Symbol={Symbol}, Signal={Signal}, Timeframe={Timeframe}, Price={Price}",
        payload.Symbol.Trim().ToUpperInvariant(),
        payload.Signal.Trim().ToUpperInvariant(),
        payload.Timeframe ?? "N/A",
        payload.Price ?? "N/A");

    var tradingSignal = new TradingSignal
    {
        Symbol = payload.Symbol.Trim().ToUpperInvariant(),
        OriginalSignal = payload.Signal.Trim().ToUpperInvariant(),
        Price = ParseDecimal(payload.Price),
        Timeframe = payload.Timeframe?.Trim(),
        Strategy = payload.Strategy?.Trim(),
        RawPayload = rawPayload,
        CreatedAtUtc = DateTime.UtcNow
    };

    dbContext.TradingSignals.Add(tradingSignal);
    await dbContext.SaveChangesAsync(cancellationToken);

    var analysisResponse = await claudeAnalysisService.AnalyzeAsync(payload, cancellationToken);

    tradingSignal.ClaudeAction = analysisResponse.Analysis?.Action;
    tradingSignal.Confidence = analysisResponse.Analysis?.Confidence;
    tradingSignal.RiskLevel = analysisResponse.Analysis?.RiskLevel;
    tradingSignal.ClaudeRawResponse = analysisResponse.RawResponse;
    tradingSignal.ShortReason = analysisResponse.IsFallback
        ? analysisResponse.Error ?? "Claude unavailable, fallback mode."
        : analysisResponse.Analysis?.ShortReason;
    tradingSignal.SuggestedStopLoss = analysisResponse.Analysis?.SuggestedStopLoss;
    tradingSignal.SuggestedTakeProfit = analysisResponse.Analysis?.SuggestedTakeProfit;

    await dbContext.SaveChangesAsync(cancellationToken);

    await telegramNotificationService.SendSignalAsync(tradingSignal, analysisResponse.IsFallback, cancellationToken);

    return TypedResults.Ok(tradingSignal);
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
        .OrderByDescending(signal => signal.CreatedAtUtc)
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(signals);
}

static async Task<Results<Ok<TradingSignal>, NotFound>> GetSignalByIdAsync(
    int id,
    TradingAgentDbContext dbContext,
    CancellationToken cancellationToken)
{
    var signal = await dbContext.TradingSignals.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
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

static decimal? ParseDecimal(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue)
        ? parsedValue
        : null;
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
