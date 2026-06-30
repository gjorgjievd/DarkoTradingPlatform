using System.Text.Json;

namespace TradingAgent.DTOs;

public sealed class UpdateSettingsRequest
{
    public Dictionary<string, JsonElement>? Settings { get; init; }
}

public sealed class SettingsResponse
{
    public required Dictionary<string, object?> Values { get; init; }
    public required IReadOnlyList<string> OverriddenKeys { get; init; }
    public required IReadOnlyList<string> EditableKeys { get; init; }
}
