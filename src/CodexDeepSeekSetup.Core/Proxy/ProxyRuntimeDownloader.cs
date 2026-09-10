using System.IO.Compression;
using System.Security.Cryptography;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Core.Proxy;

public sealed class ProxyRuntimeDownloader(HttpClient http)
{
    private const long MaximumArchiveBytes = 64L * 1024 * 1024;
    private const long MaximumExecutableBytes = 128L * 1024 * 1024;

    public async Task<OperationResult<string>> EnsureRuntimeAsync(
        ProxyRuntimeOptions options,
        IProgress<ProxyDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RuntimeDirectory);
        Directory.CreateDirectory(options.RuntimeDirectory);

        var executablePath = Path.Combine(options.RuntimeDirectory, "mihomo.exe");
        if (File.Exists(executablePath))
        {
            return OperationResult<string>.Success(executablePath);
        }

        var archivePath = Path.Combine(options.RuntimeDirectory, options.ArchiveFileName);
        ProxyRuntimeException? lastError = null;
        foreach (var source in new[] { options.PrimaryArchiveUri, options.FallbackArchiveUri })
        {
            try
            {
                await DownloadPinnedArchiveAsync(source, archivePath, options, progress, cancellationToken)
                    .ConfigureAwait(false);
                ExtractExpectedExecutable(archivePath, executablePath, options.ArchiveEntryName);
                return OperationResult<string>.Success(executablePath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ProxyRuntimeException error)
            {
                lastError = error;
                DeleteIfPresent(archivePath);
                DeleteIfPresent(archivePath + ".partial");
                DeleteIfPresent(executablePath + ".partial");
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException)
            {
                lastError = new ProxyRuntimeException("proxy.runtime.download", "Mihomo 下载或解压失败", error);
                DeleteIfPresent(archivePath);
                DeleteIfPresent(archivePath + ".partial");
                DeleteIfPresent(executablePath + ".partial");
            }
        }

        return OperationResult<string>.Failure(
            lastError?.Code ?? "proxy.runtime.download",
            lastError?.Message ?? "无法下载 Mihomo");
    }

    private async Task DownloadPinnedArchiveAsync(
        Uri source,
        string archivePath,
        ProxyRuntimeOptions options,
        IProgress<ProxyDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsConfiguredSource(source, options))
        {
            throw new ProxyRuntimeException("proxy.runtime.origin", "Mihomo 下载地址不受信任");
        }

        var partialPath = archivePath + ".partial";
        DeleteIfPresent(partialPath);
        using var response = await http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, source),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !IsAllowedFinalUri(finalUri, options))
        {
            throw new ProxyRuntimeException("proxy.runtime.origin", "Mihomo 下载被重定向到不受信任的地址");
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumArchiveBytes)
        {
            throw new ProxyRuntimeException("proxy.runtime.size", "Mihomo 下载文件超过大小限制");
        }

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                downloaded += read;
                if (downloaded > MaximumArchiveBytes)
                {
                    throw new ProxyRuntimeException("proxy.runtime.size", "Mihomo 下载文件超过大小限制");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                progress?.Report(new ProxyDownloadProgress(downloaded, response.Content.Headers.ContentLength));
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var actualHash = await CalculateSha256Async(partialPath, cancellationToken).ConfigureAwait(false);
        if (!actualHash.Equals(options.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProxyRuntimeException("proxy.runtime.hash", "Mihomo 文件校验失败，已拒绝运行");
        }

        File.Move(partialPath, archivePath, overwrite: true);
    }

    private static void ExtractExpectedExecutable(string archivePath, string executablePath, string expectedEntryName)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var extractionRoot = Path.GetFullPath(Path.GetDirectoryName(executablePath)! + Path.DirectorySeparatorChar);
        foreach (var entry in archive.Entries)
        {
            var candidate = Path.GetFullPath(Path.Combine(extractionRoot, entry.FullName));
            if (!candidate.StartsWith(extractionRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ProxyRuntimeException("proxy.runtime.archive", "Mihomo 压缩包包含不安全路径");
            }
        }

        var executable = archive.Entries.SingleOrDefault(entry =>
            entry.FullName.Equals(expectedEntryName, StringComparison.Ordinal));
        if (executable is null || executable.Length <= 0 || executable.Length > MaximumExecutableBytes)
        {
            throw new ProxyRuntimeException("proxy.runtime.archive", "Mihomo 压缩包内容不符合预期");
        }

        var partialPath = executablePath + ".partial";
        DeleteIfPresent(partialPath);
        using (var input = executable.Open())
        using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            input.CopyTo(output);
        }

        File.Move(partialPath, executablePath, overwrite: true);
    }

    private static bool IsConfiguredSource(Uri uri, ProxyRuntimeOptions options) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri == options.PrimaryArchiveUri || uri == options.FallbackArchiveUri);

    private static bool IsAllowedFinalUri(Uri uri, ProxyRuntimeOptions options)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (uri.Host.Equals(options.PrimaryArchiveUri.Host, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals(options.FallbackArchiveUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> CalculateSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class ProxyRuntimeException(string code, string message, Exception? inner = null)
        : Exception(message, inner)
    {
        public string Code { get; } = code;
    }
}
