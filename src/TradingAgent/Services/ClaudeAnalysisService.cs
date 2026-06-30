using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TradingAgent.Configuration;
using TradingAgent.DTOs;
using TradingAgent.Models;

namespace TradingAgent.Services;

public sealed class ClaudeAnalysisService(
    IHttpClientFactory httpClientFactory,
    AppSettings settings,
    ILogger<ClaudeAnalysisService> logger) : IClaudeAnalysisService
{
    public const string HttpClientName = "claude";
    private const string MessagesEndpoint = "v1/messages";
    private const int MaxLoggedResponseLength = 2000;

    private const string FilterSystemPrompt =
        "You are a professional trading signal filter. TradingView webhook indicators are the PRIMARY source of truth for strategy signals (EMA9, EMA20, EMA50, RSI, volume, volumeSpike). " +
        "Yahoo Finance data is SECONDARY validation/context only: current price sanity, large price drift, market session, 52-week range, ATR/volatility, and liquidity. " +
        "NEVER reject a signal solely because Yahoo EMA/RSI differs from TradingView EMA/RSI. Mention indicator mismatches only as warnings and modest confidence reductions. " +
        "For BUY alerts, return decision BUY, WAIT, or IGNORE. Prefer WAIT over IGNORE when the setup is not clearly bad. " +
        "BUY when TradingView EMA9 > EMA20, RSI >= 50, volumeSpike >= 70, price is close to Yahoo current price, and conviction meets the session threshold. " +
        "WAIT when confirmation is weak but not clearly invalid (confidence 40-64). " +
        "IGNORE only for clear contradictions, extreme price drift, very weak BUY inputs (RSI < 45 or volumeSpike < 50), or obvious high-risk conditions. " +
        "Respond ONLY with valid JSON matching the required schema.";

    private const string FilterJsonSchema =
        """
        {
          "decision": "BUY | SELL | WAIT | IGNORE | EXIT",
          "confidence": 0-100,
          "risk": "LOW | MEDIUM | HIGH",
          "reason": "...",
          "stopLoss": number | null,
          "takeProfit": number | null,
          "riskRewardRatio": number | null,
          "positionSizePercent": number | null,
          "shouldNotify": true | false
        }
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ClaudeAnalysisResponse> AnalyzeAsync(
        TradingViewWebhookRequest signal,
        MarketContext? marketContext,
        MarketStatusDto marketStatus,
        SignalFilterContext filterContext,
        CancellationToken cancellationToken)
    {
        if (!settings.ClaudeEnabled)
        {
            logger.LogWarning("Claude analysis skipped because it is disabled.");
            return Fallback("Claude analysis is disabled.");
        }

        if (string.IsNullOrWhiteSpace(settings.ClaudeApiKey))
        {
            logger.LogWarning("Claude analysis skipped because API key is not configured.");
            return Fallback("Claude API key is not configured.");
        }

        var session = marketStatus.MarketSession;
        var threshold = marketStatus.SessionConfidenceThreshold;
        var yahooValidation = new
        {
            currentPrice = marketContext?.CurrentPrice,
            atr = marketContext?.Atr,
            week52High = marketContext?.Week52High,
            week52Low = marketContext?.Week52Low,
            currentVolume = marketContext?.CurrentVolume,
            averageVolume20 = marketContext?.AverageVolume20,
            priceDriftPercent = filterContext.PriceDriftPercent,
            maxAllowedDriftPercent = filterContext.MaxAllowedDriftPercent,
            indicatorWarnings = filterContext.IndicatorWarnings
        };

        var prompt =
            $"You are filtering a TradingView alert. Return ONLY valid JSON with this schema:\n{FilterJsonSchema}\n" +
            $"Market session: {session}\n" +
            $"Session confidence threshold: {threshold}\n" +
            $"TradingView indicators (PRIMARY): {JsonSerializer.Serialize(filterContext.TradingView, SerializerOptions)}\n" +
            $"Yahoo validation/context (SECONDARY): {JsonSerializer.Serialize(yahooValidation, SerializerOptions)}\n" +
            $"Raw TradingView payload: {JsonSerializer.Serialize(signal, SerializerOptions)}\n" +
            GetSessionGuidance(session, threshold);
        var result = await SendRequestAsync(prompt, cancellationToken, FilterSystemPrompt);

        if (!result.IsSuccess)
        {
            return new ClaudeAnalysisResponse
            {
                RawResponse = result.RawResponse,
                IsFallback = true,
                Error = result.Error
            };
        }

        return new ClaudeAnalysisResponse
        {
            Analysis = result.Parsed,
            RawResponse = result.RawResponse
        };
    }

    public async Task<ClaudeTestResult> TestAsync(CancellationToken cancellationToken)
    {
        if (!settings.ClaudeEnabled)
        {
            return new ClaudeTestResult
            {
                Success = false,
                Model = settings.ClaudeModel,
                Error = "Claude analysis is disabled."
            };
        }

        if (string.IsNullOrWhiteSpace(settings.ClaudeApiKey))
        {
            return new ClaudeTestResult
            {
                Success = false,
                Model = settings.ClaudeModel,
                Error = "Claude API key is not configured."
            };
        }

        var result = await SendRequestAsync(
            $"You are filtering a TradingView BUY alert for NVDA at 199.20 on the 1H timeframe. Return ONLY valid JSON with this schema:\n{FilterJsonSchema}",
            cancellationToken,
            FilterSystemPrompt);

        return new ClaudeTestResult
        {
            Success = result.IsSuccess,
            HttpStatusCode = result.StatusCode,
            Model = settings.ClaudeModel,
            RawResponse = result.RawResponse,
            ParsedResponse = result.Parsed,
            ElapsedMilliseconds = result.ElapsedMilliseconds,
            Error = result.Error
        };
    }

    private async Task<ClaudeRequestResult> SendRequestAsync(
        string userPrompt,
        CancellationToken cancellationToken,
        string? systemPrompt = null)
    {
        var maxAttempts = Math.Max(settings.ClaudeMaxRetries, 0) + 1;
        ClaudeRequestResult? lastResult = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            lastResult = await SendRequestOnceAsync(userPrompt, cancellationToken, systemPrompt, attempt);

            if (lastResult.IsSuccess)
            {
                return lastResult;
            }

            if (attempt < maxAttempts)
            {
                logger.LogWarning(
                    "Claude request attempt {Attempt}/{MaxAttempts} failed: {Error}. Retrying...",
                    attempt,
                    maxAttempts,
                    lastResult.Error);
            }
        }

        return lastResult!;
    }

    private async Task<ClaudeRequestResult> SendRequestOnceAsync(
        string userPrompt,
        CancellationToken cancellationToken,
        string? systemPrompt,
        int attempt)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, MessagesEndpoint);
            request.Headers.Add("x-api-key", settings.ClaudeApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var promptPayload = new
            {
                model = settings.ClaudeModel,
                max_tokens = 1024,
                system = systemPrompt ?? "You are a professional trading assistant. Respond ONLY with valid JSON.",
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            var requestBody = JsonSerializer.Serialize(promptPayload, SerializerOptions);
            logger.LogInformation(
                "Claude request started. Attempt={Attempt}, Model={Model}, Endpoint={Endpoint}, PromptLength={PromptLength}",
                attempt,
                settings.ClaudeModel,
                MessagesEndpoint,
                userPrompt.Length);

            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(CancellationToken.None);
            stopwatch.Stop();

            logger.LogInformation(
                "Claude response received. Attempt={Attempt}, StatusCode={StatusCode}, ElapsedMs={ElapsedMs}",
                attempt,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                return new ClaudeRequestResult
                {
                    StatusCode = (int)response.StatusCode,
                    RawResponse = responseContent,
                    ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    Error = $"Claude request failed with status {(int)response.StatusCode}."
                };
            }

            var analysis = TryParseClaudeResponse(responseContent);
            if (analysis is null)
            {
                logger.LogWarning("Claude response could not be parsed into the expected JSON schema.");
                return new ClaudeRequestResult
                {
                    StatusCode = (int)response.StatusCode,
                    RawResponse = responseContent,
                    ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    Error = "Claude response could not be parsed."
                };
            }

            Normalize(analysis);

            return new ClaudeRequestResult
            {
                IsSuccess = true,
                StatusCode = (int)response.StatusCode,
                RawResponse = responseContent,
                Parsed = analysis,
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds
            };
        }
        catch (OperationCanceledException exception)
        {
            stopwatch.Stop();
            logger.LogWarning(exception, "Claude analysis timed out or was canceled. Attempt={Attempt}", attempt);
            return new ClaudeRequestResult
            {
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                Error = "Claude analysis timeout/failure"
            };
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogError(exception, "Claude analysis failed unexpectedly. Attempt={Attempt}", attempt);
            return new ClaudeRequestResult
            {
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                Error = "Claude analysis failed unexpectedly."
            };
        }
    }

    private static string GetSessionGuidance(string marketSession, int threshold)
    {
        if (marketSession == MarketSessionValues.Regular)
        {
            return $"Regular session: use TradingView indicators as primary. Threshold for BUY notify is {threshold}.";
        }

        return
            $"Extended-hours session ({marketSession}): Yahoo is context only. Threshold for BUY notify is {threshold}. " +
            "Be mindful of liquidity/session risk, but do not auto-reject because Yahoo EMA/RSI differs from TradingView.";
    }

    private static ClaudeAnalysisResponse Fallback(string error)
        => new() { IsFallback = true, Error = error };

    private static ClaudeAnalysisResult? TryParseClaudeResponse(string responseContent)
    {
        try
        {
            using var document = JsonDocument.Parse(responseContent);
            if (!document.RootElement.TryGetProperty("content", out var contentElement)
                || contentElement.ValueKind != JsonValueKind.Array
                || contentElement.GetArrayLength() == 0)
            {
                return null;
            }

            var firstContent = contentElement[0];
            if (!firstContent.TryGetProperty("text", out var textElement))
            {
                return null;
            }

            var text = textElement.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var jsonPayload = ExtractJsonObject(text);
            return string.IsNullOrWhiteSpace(jsonPayload)
                ? null
                : TryDeserializeAnalysis(jsonPayload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ClaudeAnalysisResult? TryDeserializeAnalysis(string jsonPayload)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonPayload);
            var root = document.RootElement;

            if (!root.TryGetProperty("decision", out _) && !root.TryGetProperty("action", out _))
            {
                if (root.TryGetProperty("analysis", out var analysisElement) && analysisElement.ValueKind == JsonValueKind.Object)
                {
                    root = analysisElement;
                }
            }

            return new ClaudeAnalysisResult
            {
                Action = GetString(root, "decision") ?? GetString(root, "action") ?? GetString(root, "recommendation"),
                Confidence = GetInt(root, "confidence"),
                ShortReason = GetString(root, "reason") ?? GetString(root, "shortReason"),
                RiskLevel = GetString(root, "risk") ?? GetString(root, "riskLevel"),
                SuggestedStopLoss = GetDecimal(root, "stopLoss") ?? GetDecimal(root, "suggestedStopLoss"),
                SuggestedTakeProfit = GetDecimal(root, "takeProfit") ?? GetDecimal(root, "suggestedTakeProfit"),
                RiskRewardRatio = GetDecimal(root, "riskRewardRatio"),
                PositionSizePercent = GetDecimal(root, "positionSizePercent"),
                ShouldNotify = GetBool(root, "shouldNotify")
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int? GetInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (element.TryGetDecimal(out var decimalValue))
            {
                return NormalizeConfidence(decimalValue);
            }
        }

        if (element.ValueKind == JsonValueKind.String
            && decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDecimal))
        {
            return NormalizeConfidence(parsedDecimal);
        }

        return null;
    }

    private static int? NormalizeConfidence(decimal value)
    {
        if (value is > 0 and <= 1)
        {
            return (int)Math.Round(value * 100, MidpointRounding.AwayFromZero);
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static decimal? GetDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (element.ValueKind == JsonValueKind.String
            && decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? GetBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string? ExtractJsonObject(string value)
    {
        var startIndex = value.IndexOf('{');
        var endIndex = value.LastIndexOf('}');

        return startIndex >= 0 && endIndex > startIndex
            ? value[startIndex..(endIndex + 1)]
            : null;
    }

    private static void Normalize(ClaudeAnalysisResult result)
    {
        result.Action = NormalizeToken(result.Action, "WAIT", ["BUY", "SELL", "WAIT", "IGNORE", "EXIT"]);
        result.RiskLevel = NormalizeToken(result.RiskLevel, "MEDIUM", ["LOW", "MEDIUM", "HIGH"]);
        result.Confidence = result.Confidence.HasValue ? Math.Clamp(result.Confidence.Value, 0, 100) : null;
        result.SuggestedStopLoss = NormalizeDecimal(result.SuggestedStopLoss);
        result.SuggestedTakeProfit = NormalizeDecimal(result.SuggestedTakeProfit);
        result.RiskRewardRatio = NormalizeDecimal(result.RiskRewardRatio);
        result.PositionSizePercent = result.PositionSizePercent.HasValue
            ? Math.Clamp(result.PositionSizePercent.Value, 0, 100)
            : null;
        result.ShortReason = string.IsNullOrWhiteSpace(result.ShortReason) ? "No reason provided." : result.ShortReason.Trim();

        if (!result.ShouldNotify.HasValue)
        {
            result.ShouldNotify = result.Action is "BUY" or "SELL"
                && result.Confidence >= 70;
        }
    }

    private static string NormalizeToken(string? value, string fallback, string[] allowedValues)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is not null && allowedValues.Contains(normalized) ? normalized : fallback;
    }

    private static decimal? NormalizeDecimal(decimal? value)
        => value.HasValue ? decimal.Parse(value.Value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) : null;

    private static string TrimForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= MaxLoggedResponseLength
            ? value
            : value[..MaxLoggedResponseLength] + "...(truncated)";
    }

    private sealed class ClaudeRequestResult
    {
        public bool IsSuccess { get; init; }
        public int StatusCode { get; init; }
        public string? RawResponse { get; init; }
        public ClaudeAnalysisResult? Parsed { get; init; }
        public double ElapsedMilliseconds { get; init; }
        public string? Error { get; init; }
    }
}
