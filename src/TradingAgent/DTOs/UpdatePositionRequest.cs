namespace TradingAgent.DTOs;

public sealed class UpdatePositionRequest
{
    public string? Notes { get; init; }
}

public sealed class ClosePositionRequest
{
    public decimal? ExitPrice { get; init; }
}
