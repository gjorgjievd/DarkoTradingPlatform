namespace TradingAgent.DTOs;

public sealed class UpdateTradingSignalRequest
{
    public decimal? ProfitLoss { get; init; }
    public string? Notes { get; init; }
    public bool? IsClosed { get; init; }
}
