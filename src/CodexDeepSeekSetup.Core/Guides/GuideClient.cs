using System.Net;
using System.Text.Json;

namespace CodexDeepSeekSetup.Core.Guides;

public sealed class GuideClient : IGuideClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(3);
    private readonly HttpClient http;
    private readonly TimeSpan requestTimeout;

    public GuideClient(HttpClient http, Uri baseAddress, TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (!IsHttps(baseAddress))
        {
            throw new ArgumentException("引导服务必须使用 HTTPS。", nameof(baseAddress));
        }

        this.http = http;
        this.http.BaseAddress ??= baseAddress;
        this.requestTimeout = requestTimeout ?? DefaultRequestTimeout;
        if (this.requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }
    }

    public async Task<GuideDocument?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(requestTimeout);
            using var response = await http.GetAsync("api/v1/guide", timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NoContent || !response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var envelope = await JsonSerializer.DeserializeAsync<GuideEnvelope>(stream, cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            return Validate(envelope);
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

    private static GuideDocument? Validate(GuideEnvelope? envelope)
    {
        if (envelope is null || envelope.Version != 1 || !envelope.Enabled ||
            !ValidRequired(envelope.Title, 80) || envelope.Steps is not { Count: >= 1 and <= 8 })
        {
            return null;
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var steps = new List<GuideStep>(envelope.Steps.Count);
        foreach (var source in envelope.Steps)
        {
            var id = source.Id.Trim();
            if (!ValidRequired(id, 48) || !identifiers.Add(id) ||
                !ValidRequired(source.Title, 60) || !ValidRequired(source.Body, 240) ||
                !ValidRequired(source.CompletionHint, 120) || !ValidRequired(source.ActionText, 16) ||
                !TryHttpsUri(source.ActionUrl, out var actionUrl))
            {
                return null;
            }

            Uri? imageUrl = null;
            if (!string.IsNullOrWhiteSpace(source.ImageUrl) && !TryHttpsUri(source.ImageUrl, out imageUrl))
            {
                return null;
            }

            steps.Add(new GuideStep(
                id,
                source.Title.Trim(),
                source.Body.Trim(),
                source.CompletionHint.Trim(),
                source.ActionText.Trim(),
                actionUrl!,
                imageUrl));
        }

        return new GuideDocument(envelope.Title.Trim(), steps.AsReadOnly());
    }

    private static bool ValidRequired(string? value, int maximumCharacters) =>
        !string.IsNullOrWhiteSpace(value) && value.EnumerateRunes().Count() <= maximumCharacters;

    private static bool TryHttpsUri(string? value, out Uri? uri)
    {
        var valid = Uri.TryCreate(value, UriKind.Absolute, out uri) && IsHttps(uri!);
        if (!valid)
        {
            uri = null;
        }
        return valid;
    }

    private static bool IsHttps(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host);
}
