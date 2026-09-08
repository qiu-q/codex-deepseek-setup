using System.Text.Json.Serialization;

namespace CodexDeepSeekSetup.Core.DeepSeek;

public sealed record DeepSeekAccountStatus(bool IsAvailable, IReadOnlyList<DeepSeekBalance> Balances);

public sealed record DeepSeekBalance(
    string Currency,
    string TotalBalance,
    string GrantedBalance,
    string ToppedUpBalance);

internal sealed class BalanceResponse
{
    [JsonPropertyName("is_available")]
    public bool IsAvailable { get; init; }

    [JsonPropertyName("balance_infos")]
    public List<BalanceInfo> BalanceInfos { get; init; } = [];
}

internal sealed class BalanceInfo
{
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    [JsonPropertyName("total_balance")]
    public string TotalBalance { get; init; } = string.Empty;

    [JsonPropertyName("granted_balance")]
    public string GrantedBalance { get; init; } = string.Empty;

    [JsonPropertyName("topped_up_balance")]
    public string ToppedUpBalance { get; init; } = string.Empty;
}

internal sealed class ResponseEnvelope
{
    [JsonPropertyName("output")]
    public List<ResponseOutputItem> Output { get; init; } = [];
}

internal sealed class ResponseOutputItem
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public List<ResponseContentPart> Content { get; init; } = [];
}

internal sealed class ResponseContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}
