using System.Net;
using System.Net.Http.Headers;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Core.Downloads;

public sealed class OfficialDownloadService(HttpClient http, OfficialOriginPolicy originPolicy)
{
    public async Task<OperationResult<CodexPayload>> DownloadCodexPayloadAsync(
        string cacheDirectory,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        Directory.CreateDirectory(cacheDirectory);

        var msixUri = new Uri(OfficialPayloadUrls.CodexMsix);
        var licenseUri = new Uri(OfficialPayloadUrls.OfflineLicense);
        var allowedMsix = originPolicy.EnsureAllowed(msixUri);
        var allowedLicense = originPolicy.EnsureAllowed(licenseUri);
        if (!allowedMsix.IsSuccess || !allowedLicense.IsSuccess)
        {
            return OperationResult<CodexPayload>.Failure("download.origin.rejected", "官方下载地址验证失败");
        }

        var msixPath = Path.Combine(cacheDirectory, "ChatGPT-x64.msix");
        var licensePath = Path.Combine(cacheDirectory, "ChatGPT-License.xml");
        try
        {
            await Task.WhenAll(
                DownloadFileAsync(msixUri, msixPath, progress, cancellationToken),
                DownloadFileAsync(licenseUri, licensePath, progress, cancellationToken)).ConfigureAwait(false);
            return OperationResult<CodexPayload>.Success(new CodexPayload(msixPath, licensePath));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return OperationResult<CodexPayload>.Failure("download.network.failed", "无法从 OpenAI 官方地址下载文件");
        }
        catch (IOException)
        {
            return OperationResult<CodexPayload>.Failure("download.file.failed", "下载文件无法写入缓存目录");
        }
    }

    private async Task DownloadFileAsync(
        Uri uri,
        string destination,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await DownloadFileOnceAsync(uri, destination, progress, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error) when (error is HttpRequestException or IOException)
            {
                lastError = error;
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(150 * Math.Pow(2, attempt - 1)), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        throw lastError ?? new HttpRequestException("Download failed");
    }

    private async Task DownloadFileOnceAsync(
        Uri uri,
        string destination,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partial = destination + ".partial";
        var existingLength = File.Exists(partial) ? new FileInfo(partial).Length : 0L;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var isResume = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!isResume)
        {
            existingLength = 0;
        }

        var total = response.Content.Headers.ContentRange?.Length
            ?? (response.Content.Headers.ContentLength is long contentLength ? existingLength + contentLength : null);
        var mode = isResume ? FileMode.Append : FileMode.Create;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(partial, mode, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        var downloaded = existingLength;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            downloaded += read;
            progress?.Report(new DownloadProgress(Path.GetFileName(destination), downloaded, total));
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Close();
        File.Move(partial, destination, overwrite: true);
    }
}
