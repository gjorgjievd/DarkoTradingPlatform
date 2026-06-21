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

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClaudeAnalysisResponse> AnalyzeAsync(TradingViewWebhookRequest signal, CancellationToken cancellationToken)
    {
        if (!settings.ClaudeEnabled)
        {
            return new ClaudeAnalysisResponse
            {
                IsFallback = true,
                Error = "Claude analysis is disabled."
            };
        }

        if (string.IsNullOrWhiteSpace(settings.ClaudeApiKey))
        {
            return new ClaudeAnalysisResponse
            {
                IsFallback = true,
                Error = "Claude API key is not configured."
            };
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages");
            request.Headers.Add("x-api-key", settings.ClaudeApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var promptPayload = new
            {
                model = settings.ClaudeModel,
                max_tokens = 400,
                system =
                    "You are a trading signal assistant. Respond with only one JSON object with keys: action, confidence, shortReason, riskLevel, suggestedStopLoss, suggestedTakeProfit. action must be BUY, SELL, WAIT, or IGNORE. confidence must be 0-100. riskLevel must be LOW, MEDIUM, or HIGH.",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = $"Analyze this trading signal and return JSON only: {JsonSerializer.Serialize(signal, SerializerOptions)}"
                    }
                }
            };

            request.Content = new StringContent(JsonSerializer.Serialize(promptPayload), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Claude request failed with status code {StatusCode}.", (int)response.StatusCode);
                return new ClaudeAnalysisResponse
                {
                    RawResponse = responseContent,
                    IsFallback = true,
                    Error = $"Claude request failed with status {(int)response.StatusCode}."
                };
            }

            var analysis = TryParseClaudeResponse(responseContent);
            if (analysis is null)
            {
                logger.LogWarning("Claude response could not be parsed into the expected JSON schema.");
                return new ClaudeAnalysisResponse
                {
                    RawResponse = responseContent,
                    IsFallback = true,
                    Error = "Claude response could not be parsed."
                };
            }

            Normalize(analysis);

            return new ClaudeAnalysisResponse
            {
                Analysis = analysis,
                RawResponse = responseContent
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Claude analysis failed unexpectedly.");
            return new ClaudeAnalysisResponse
            {
                IsFallback = true,
                Error = "Claude analysis failed unexpectedly."
            };
        }
    }

    private static ClaudeAnalysisResult? TryParseClaudeResponse(string responseContent)
    {
        using var document = JsonDocument.Parse(responseContent);
        if (!document.RootElement.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var combinedText = new StringBuilder();
        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var typeElement)
                && typeElement.GetString() == "text"
                && item.TryGetProperty("text", out var textElement))
            {
                combinedText.Append(textElement.GetString());
            }
        }

        var text = combinedText.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var jsonPayload = ExtractJsonObject(text);
        return string.IsNullOrWhiteSpace(jsonPayload)
            ? null
            : JsonSerializer.Deserialize<ClaudeAnalysisResult>(jsonPayload, SerializerOptions);
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
}
