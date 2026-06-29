namespace TradingAgent.Models;

public static class PositionStatus
{
    public const string Open = "OPEN";
    public const string Closed = "CLOSED";
}

public sealed class Position
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Status { get; set; } = PositionStatus.Open;
    public int EntrySignalId { get; set; }
    public int? ExitSignalId { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal Quantity { get; set; }
    public DateTime EntryTimeUtc { get; set; }
    public DateTime? ExitTimeUtc { get; set; }
    public decimal? ProfitLoss { get; set; }
    public decimal? ProfitLossPercent { get; set; }
    public decimal? MaxRiskPercent { get; set; }
    public string? EntryMarketSession { get; set; }
    public string? Notes { get; set; }
}
