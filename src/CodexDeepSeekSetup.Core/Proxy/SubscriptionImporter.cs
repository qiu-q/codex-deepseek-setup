using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Core.Proxy;

public sealed class SubscriptionImporter(HttpClient http)
{
    public const int MaximumBytes = 8 * 1024 * 1024;

    public async Task<OperationResult<string>> ImportAsync(
        Uri subscriptionUri,
        string destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscriptionUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (subscriptionUri.Scheme != Uri.UriSchemeHttps)
        {
            return OperationResult<string>.Failure("proxy.subscription.url", "订阅地址必须使用 HTTPS");
        }

        var partialPath = destination + ".partial";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, subscriptionUri);
            request.Headers.UserAgent.ParseAdd("clash.meta");
            using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            {
                return OperationResult<string>.Failure("proxy.subscription.url", "订阅下载被重定向到非 HTTPS 地址");
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumBytes)
            {
                return OperationResult<string>.Failure(
                    "proxy.subscription.size",
                    "订阅内容超过 8 MB");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            DeleteIfPresent(partialPath);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaximumBytes)
                    {
                        return OperationResult<string>.Failure("proxy.subscription.size", "订阅内容超过 8 MB");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(partialPath).Length == 0 || await LooksLikeHtmlAsync(partialPath, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult<string>.Failure("proxy.subscription.content", "订阅内容为空或返回了网页");
            }

            File.Move(partialPath, destination, overwrite: true);
            return OperationResult<string>.Success(destination);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException error)
        {
            return OperationResult<string>.Failure(
                "proxy.subscription.network",
                "无法下载订阅内容",
                DescribeFailure(subscriptionUri, error));
        }
        catch (IOException error)
        {
            return OperationResult<string>.Failure(
                "proxy.subscription.file",
                "无法保存订阅内容",
                DescribeFailure(subscriptionUri, error));
        }
        finally
        {
            DeleteIfPresent(partialPath);
        }
    }

    private static string DescribeFailure(Uri subscriptionUri, Exception error)
    {
        var endpoint = subscriptionUri.GetLeftPart(UriPartial.Authority) + subscriptionUri.AbsolutePath;
        var statusCode = (error as HttpRequestException)?.StatusCode;
        var status = statusCode is { } actualStatus
            ? $" HTTP {(int)actualStatus} ({actualStatus})"
            : string.Empty;
        return $"endpoint={endpoint}; exception={error.GetType().Name};{status}; message={error.Message}";
    }

    private static async Task<bool> LooksLikeHtmlAsync(string path, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(512, checked((int)new FileInfo(path).Length))];
        await using var stream = File.OpenRead(path);
        var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        var prefix = System.Text.Encoding.UTF8.GetString(buffer, 0, read).TrimStart();
        return prefix.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
               prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
