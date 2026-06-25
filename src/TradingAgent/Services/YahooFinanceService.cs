using System.Globalization;
using System.Text.Json;
using TradingAgent.DTOs;

namespace TradingAgent.Services;

public sealed class YahooFinanceService(
    IHttpClientFactory httpClientFactory,
    ILogger<YahooFinanceService> logger) : IYahooFinanceService
{
    public const string HttpClientName = "yahoo-finance";

    public async Task<MarketContext> FetchMarketContextAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var requestUri =
                $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(normalizedSymbol)}?interval=1d&range=1y";

            logger.LogInformation("Yahoo Finance request started. Symbol={Symbol}", normalizedSymbol);

            using var response = await client.GetAsync(requestUri, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Yahoo Finance request failed. Symbol={Symbol}, StatusCode={StatusCode}",
                    normalizedSymbol,
                    (int)response.StatusCode);

                return FailedContext(normalizedSymbol, $"Yahoo Finance request failed with status {(int)response.StatusCode}.");
            }

            var context = ParseChartResponse(normalizedSymbol, responseBody);
            logger.LogInformation(
                "Yahoo Finance data fetched. Symbol={Symbol}, CurrentPrice={CurrentPrice}",
                normalizedSymbol,
                context.CurrentPrice);

            return context;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Yahoo Finance request failed unexpectedly. Symbol={Symbol}", normalizedSymbol);
            return FailedContext(normalizedSymbol, "Yahoo Finance request failed unexpectedly.");
        }
    }

    private static MarketContext FailedContext(string symbol, string error)
        => new()
        {
            Symbol = symbol,
            FetchedAtUtc = DateTime.UtcNow,
            Error = error
        };

    private static MarketContext ParseChartResponse(string symbol, string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (!root.TryGetProperty("chart", out var chart)
            || !chart.TryGetProperty("result", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
        {
            return FailedContext(symbol, "Yahoo Finance response did not contain chart data.");
        }

        var result = results[0];
        if (!result.TryGetProperty("indicators", out var indicators)
            || !indicators.TryGetProperty("quote", out var quotes)
            || quotes.ValueKind != JsonValueKind.Array
            || quotes.GetArrayLength() == 0)
        {
            return FailedContext(symbol, "Yahoo Finance response did not contain quote indicators.");
        }

        var quote = quotes[0];
        var closes = ReadDecimalSeries(quote, "close");
        var highs = ReadDecimalSeries(quote, "high");
        var lows = ReadDecimalSeries(quote, "low");
        var volumes = ReadLongSeries(quote, "volume");

        if (closes.Count == 0)
        {
            return FailedContext(symbol, "Yahoo Finance response did not contain closing prices.");
        }

        decimal? currentPrice = closes[^1];
        decimal? week52High = null;
        decimal? week52Low = null;

        if (result.TryGetProperty("meta", out var meta))
        {
            currentPrice = ReadDecimal(meta, "regularMarketPrice") ?? currentPrice;
            week52High = ReadDecimal(meta, "fiftyTwoWeekHigh");
            week52Low = ReadDecimal(meta, "fiftyTwoWeekLow");
        }

        week52High ??= highs.Count > 0 ? highs.Max() : null;
        week52Low ??= lows.Count > 0 ? lows.Min() : null;

        var (macd, macdSignal) = TechnicalIndicators.CalculateMacd(closes);

        return new MarketContext
        {
            Symbol = symbol,
            CurrentPrice = currentPrice,
            Ema9 = TechnicalIndicators.CalculateEma(closes, 9),
            Ema20 = TechnicalIndicators.CalculateEma(closes, 20),
            Ema50 = TechnicalIndicators.CalculateEma(closes, 50),
            Rsi14 = TechnicalIndicators.CalculateRsi(closes),
            Macd = macd,
            MacdSignal = macdSignal,
            Atr = TechnicalIndicators.CalculateAtr(highs, lows, closes),
            CurrentVolume = volumes.Count > 0 ? volumes[^1] : null,
            AverageVolume20 = TechnicalIndicators.CalculateAverageVolume(volumes),
            Week52High = week52High,
            Week52Low = week52Low,
            FetchedAtUtc = DateTime.UtcNow
        };
    }

    private static List<decimal> ReadDecimalSeries(JsonElement parent, string propertyName)
    {
        var values = new List<decimal>();
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (item.TryGetDecimal(out var decimalValue))
            {
                values.Add(decimalValue);
                continue;
            }

            if (item.ValueKind == JsonValueKind.Number
                && decimal.TryParse(item.GetRawText(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                values.Add(parsed);
            }
        }

        return values;
    }

    private static List<long> ReadLongSeries(JsonElement parent, string propertyName)
    {
        var values = new List<long>();
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (item.TryGetInt64(out var longValue))
            {
                values.Add(longValue);
            }
        }

        return values;
    }

    private static decimal? ReadDecimal(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        return decimal.TryParse(element.GetRawText(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
