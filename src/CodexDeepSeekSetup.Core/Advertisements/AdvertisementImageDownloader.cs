namespace CodexDeepSeekSetup.Core.Advertisements;

public sealed class AdvertisementImageDownloader(HttpClient http)
{
    public const int MaximumImageBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp"
    };

    public async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentType?.MediaType is not { } mediaType ||
                !AllowedContentTypes.Contains(mediaType) ||
                response.Content.Headers.ContentLength > MaximumImageBytes)
            {
                return null;
            }

            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[32 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                if (output.Length + read > MaximumImageBytes)
                {
                    return null;
                }
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
