using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TradingAgent.Configuration;
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

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ClaudeAnalysisResponse> AnalyzeAsync(TradingViewWebhookRequest signal, CancellationToken cancellationToken)
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

        var prompt =
            $"Analyze this {signal.Signal?.Trim().ToUpperInvariant()} signal for {signal.Symbol?.Trim().ToUpperInvariant()} " +
            $"and return a single JSON object with keys: action, confidence, reason, risk, suggestedStopLoss, suggestedTakeProfit. " +
            $"action must be BUY, SELL, WAIT, or IGNORE. confidence must be 0-100. risk must be LOW, MEDIUM, or HIGH. " +
            $"Signal data: {JsonSerializer.Serialize(signal, SerializerOptions)}";
        var result = await SendRequestAsync(prompt, cancellationToken);

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
            "Analyze this BUY signal for NVDA at price 199.20 on the 1H timeframe. Return JSON with action, confidence, reason, risk, suggestedStopLoss, suggestedTakeProfit.",
            cancellationToken);

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

    private async Task<ClaudeRequestResult> SendRequestAsync(string userPrompt, CancellationToken cancellationToken)
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
                system = "You are a professional trading assistant. Respond ONLY with valid JSON.",
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            var requestBody = JsonSerializer.Serialize(promptPayload, SerializerOptions);
            logger.LogInformation(
                "Claude request started. Model={Model}, Endpoint={Endpoint}, PromptLength={PromptLength}",
                settings.ClaudeModel,
                MessagesEndpoint,
                userPrompt.Length);

            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            logger.LogInformation(
                "Claude response received. StatusCode={StatusCode}, ElapsedMs={ElapsedMs}, Body={Body}",
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                TrimForLog(responseContent));

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
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogError(exception, "Claude analysis failed unexpectedly.");
            return new ClaudeRequestResult
            {
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                Error = "Claude analysis failed unexpectedly."
            };
        }
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

            if (!root.TryGetProperty("action", out _))
            {
                if (root.TryGetProperty("analysis", out var analysisElement) && analysisElement.ValueKind == JsonValueKind.Object)
                {
                    root = analysisElement;
                }
            }

            return new ClaudeAnalysisResult
            {
                Action = GetString(root, "action") ?? GetString(root, "recommendation"),
                Confidence = GetInt(root, "confidence"),
                ShortReason = GetString(root, "reason") ?? GetString(root, "shortReason"),
                RiskLevel = GetString(root, "risk") ?? GetString(root, "riskLevel"),
                SuggestedStopLoss = GetDecimal(root, "suggestedStopLoss"),
                SuggestedTakeProfit = GetDecimal(root, "suggestedTakeProfit")
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
        result.Action = NormalizeToken(result.Action, "WAIT", ["BUY", "SELL", "WAIT", "IGNORE"]);
        result.RiskLevel = NormalizeToken(result.RiskLevel, "MEDIUM", ["LOW", "MEDIUM", "HIGH"]);
        result.Confidence = result.Confidence.HasValue ? Math.Clamp(result.Confidence.Value, 0, 100) : null;
        result.SuggestedStopLoss = NormalizeDecimal(result.SuggestedStopLoss);
        result.SuggestedTakeProfit = NormalizeDecimal(result.SuggestedTakeProfit);
        result.ShortReason = string.IsNullOrWhiteSpace(result.ShortReason) ? "No reason provided." : result.ShortReason.Trim();
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
