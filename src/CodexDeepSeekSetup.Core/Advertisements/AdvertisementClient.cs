using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CodexDeepSeekSetup.Core.Advertisements;

public sealed class AdvertisementClient : IAdvertisementClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private readonly HttpClient http;
    private readonly Func<DateTimeOffset> clock;
    private readonly HashSet<string> recordedImpressions = new(StringComparer.Ordinal);
    private readonly object impressionLock = new();

    public AdvertisementClient(HttpClient http, Uri baseAddress, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (baseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("推广服务必须使用 HTTPS。", nameof(baseAddress));
        }

        this.http = http;
        this.http.BaseAddress ??= baseAddress;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<AdCampaign?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var response = await http.GetAsync("api/v1/ad/current", timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NoContent || !response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<AdCampaignEnvelope>(stream, cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            return Validate(payload, clock());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task TrackAsync(string campaignId, AdEventType eventType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaignId) || campaignId.Length > 80)
        {
            return;
        }

        if (eventType == AdEventType.Impression)
        {
            lock (impressionLock)
            {
                if (!recordedImpressions.Add(campaignId))
                {
                    return;
                }
            }
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var response = await http.PostAsJsonAsync(
                "api/v1/events",
                new AdEventEnvelope(campaignId, eventType == AdEventType.Impression ? "impression" : "click"),
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpRequestException)
        {
        }
    }

    private static AdCampaign? Validate(AdCampaignEnvelope? payload, DateTimeOffset now)
    {
        if (payload is null || payload.Version != 1 || !payload.Enabled ||
            string.IsNullOrWhiteSpace(payload.CampaignId) || payload.CampaignId.Length > 80 ||
            string.IsNullOrWhiteSpace(payload.Title) || payload.Title.Length > 60 ||
            string.IsNullOrWhiteSpace(payload.Body) || payload.Body.Length > 180 ||
            string.IsNullOrWhiteSpace(payload.CtaText) || payload.CtaText.Length > 16 ||
            !TryHttpsUri(payload.TargetUrl, out var target) ||
            (payload.StartsAt is not null && now < payload.StartsAt) ||
            (payload.EndsAt is not null && now >= payload.EndsAt))
        {
            return null;
        }

        Uri? image = null;
        if (!string.IsNullOrWhiteSpace(payload.ImageUrl) && !TryHttpsUri(payload.ImageUrl, out image))
        {
            image = null;
        }

        return new AdCampaign(
            payload.CampaignId.Trim(),
            payload.Title.Trim(),
            payload.Body.Trim(),
            payload.CtaText.Trim(),
            target!,
            image,
            payload.StartsAt,
            payload.EndsAt);
    }

    private static bool TryHttpsUri(string? value, out Uri? uri)
    {
        var valid = Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps;
        if (!valid)
        {
            uri = null;
        }
        return valid;
    }
}
