using System.Text.Json.Serialization;

namespace CodexDeepSeekSetup.Core.Advertisements;

public enum AdEventType
{
    Impression,
    Click
}

public sealed record AdCampaign(
    string CampaignId,
    string Title,
    string Body,
    string CtaText,
    Uri TargetUrl,
    Uri? ImageUrl,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

public interface IAdvertisementClient
{
    Task<AdCampaign?> GetCurrentAsync(CancellationToken cancellationToken);
    Task TrackAsync(string campaignId, AdEventType eventType, CancellationToken cancellationToken);
}

internal sealed class AdCampaignEnvelope
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("campaignId")]
    public string CampaignId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("ctaText")]
    public string CtaText { get; init; } = string.Empty;

    [JsonPropertyName("targetUrl")]
    public string TargetUrl { get; init; } = string.Empty;

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("startsAt")]
    public DateTimeOffset? StartsAt { get; init; }

    [JsonPropertyName("endsAt")]
    public DateTimeOffset? EndsAt { get; init; }
}

internal sealed record AdEventEnvelope(
    [property: JsonPropertyName("campaignId")] string CampaignId,
    [property: JsonPropertyName("event")] string Event);
