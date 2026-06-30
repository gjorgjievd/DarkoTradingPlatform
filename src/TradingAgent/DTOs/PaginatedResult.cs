namespace TradingAgent.DTOs;

public sealed class PaginatedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public int TotalPages { get; init; }
}

public static class PaginationHelper
{
    public static bool WantsPagination(int? page, int? pageSize) => page.HasValue || pageSize.HasValue;

    public static (int Page, int PageSize) Resolve(int? page, int? pageSize)
    {
        var resolvedPage = Math.Max(page ?? 1, 1);
        var resolvedPageSize = pageSize switch
        {
            null or < 1 => 25,
            > 100 => 100,
            _ => pageSize.Value
        };

        return (resolvedPage, resolvedPageSize);
    }

    public static PaginatedResult<T> Create<T>(IReadOnlyList<T> items, int page, int pageSize, int total)
    {
        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new PaginatedResult<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = totalPages
        };
    }

    public static DateTime? ParseUtcDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : parsed.ToUniversalTime();
        }

        return null;
    }
}

public sealed class PositionListItem
{
    public int Id { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int EntrySignalId { get; init; }
    public int? ExitSignalId { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public decimal Quantity { get; init; }
    public DateTime EntryTimeUtc { get; init; }
    public DateTime? ExitTimeUtc { get; init; }
    public decimal? ProfitLoss { get; init; }
    public decimal? ProfitLossPercent { get; init; }
    public decimal? MaxRiskPercent { get; init; }
    public string? EntryMarketSession { get; init; }
    public string? Notes { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
}
