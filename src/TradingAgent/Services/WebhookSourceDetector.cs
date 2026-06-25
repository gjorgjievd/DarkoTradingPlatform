using System.Text.Json;
using TradingAgent.Models;

namespace TradingAgent.Services;

public static class WebhookSourceDetector
{
    public static (string Source, bool IsTest) Detect(
        HttpRequest request,
        string rawPayload,
        TradingViewWebhookRequest? payload)
    {
        var payloadSource = GetPayloadSource(payload, rawPayload);

        if (IsTestRequest(request, payloadSource))
        {
            var source = payloadSource is WebhookSources.CursorTest or WebhookSources.PostmanTest
                ? payloadSource
                : WebhookSources.CursorTest;
            return (source, true);
        }

        if (IsTradingViewRequest(request, payloadSource))
        {
            return (WebhookSources.TradingView, false);
        }

        return (WebhookSources.Unknown, false);
    }

    public static bool IsTestRequest(HttpRequest request, string? payloadSource)
    {
        if (string.Equals(request.Headers["X-Test-Mode"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return payloadSource is WebhookSources.CursorTest or WebhookSources.PostmanTest;
    }

    private static bool IsTradingViewRequest(HttpRequest request, string? payloadSource)
    {
        if (string.Equals(payloadSource, WebhookSources.TradingView, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var userAgent = request.Headers.UserAgent.ToString();
        return userAgent.Contains("TradingView", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetPayloadSource(TradingViewWebhookRequest? payload, string rawPayload)
    {
        if (!string.IsNullOrWhiteSpace(payload?.Source))
        {
            return payload.Source.Trim().ToUpperInvariant();
        }

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            if (document.RootElement.TryGetProperty("source", out var sourceElement))
            {
                return sourceElement.GetString()?.Trim().ToUpperInvariant();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
