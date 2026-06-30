namespace TradingAgent.Models;

public static class WebhookResultStatuses
{
    public const string Pending = "PENDING";
    public const string Success = "SUCCESS";
    public const string Ignored = "IGNORED";
    public const string AiFailed = "AI_FAILED";
    public const string Error = "ERROR";
    public const string BadRequest = "BAD_REQUEST";
    public const string Unauthorized = "UNAUTHORIZED";
}
