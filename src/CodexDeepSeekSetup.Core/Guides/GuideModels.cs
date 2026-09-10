using System.Text.Json.Serialization;

namespace CodexDeepSeekSetup.Core.Guides;

public sealed record GuideDocument(
    string Title,
    IReadOnlyList<GuideStep> Steps);

public sealed record GuideStep(
    string Id,
    string Title,
    string Body,
    string CompletionHint,
    string ActionText,
    Uri ActionUrl,
    Uri? ImageUrl);

public interface IGuideClient
{
    Task<GuideDocument?> GetCurrentAsync(CancellationToken cancellationToken);
}

internal sealed class GuideEnvelope
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<GuideStepEnvelope>? Steps { get; init; }
}

internal sealed class GuideStepEnvelope
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("completionHint")]
    public string CompletionHint { get; init; } = string.Empty;

    [JsonPropertyName("actionText")]
    public string ActionText { get; init; } = string.Empty;

    [JsonPropertyName("actionUrl")]
    public string ActionUrl { get; init; } = string.Empty;

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }
}
