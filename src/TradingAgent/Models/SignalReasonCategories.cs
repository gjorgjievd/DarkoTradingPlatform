namespace TradingAgent.Models;

public static class SignalReasonCategories
{
    public const string PriceDriftWarning = "PRICE_DRIFT_WARNING";
    public const string WeakConfirmation = "WEAK_CONFIRMATION";
    public const string NoOpenPositionForSell = "NO_OPEN_POSITION_FOR_SELL";
    public const string DuplicateBuy = "DUPLICATE_BUY";
    public const string LowConfidence = "LOW_CONFIDENCE";
    public const string SessionRisk = "SESSION_RISK";

    public static string Join(IEnumerable<string?> categories)
    {
        return string.Join(
            ",",
            categories.Where(category => !string.IsNullOrWhiteSpace(category)).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static string Join(params string?[] categories) => Join((IEnumerable<string?>)categories);
}
