using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using CodexDeepSeekSetup.Core.Proxy;

namespace CodexDeepSeekSetup.Core.Tests.Proxy;

public sealed class ProxyRuntimeDownloaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codex-proxy-runtime-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnsureRuntimeAsync_FallsBackAndExtractsOnlyPinnedArchive()
    {
        var archive = CreateArchive(("mihomo-windows-amd64-compatible.exe", "mihomo-binary"));
        var options = CreateOptions(archive);
        var handler = new RuntimeHandler(request =>
        {
            if (request.RequestUri == options.PrimaryArchiveUri)
            {
                throw new HttpRequestException("mirror unavailable");
            }

            return Response(request, archive);
        });

        var result = await new ProxyRuntimeDownloader(new HttpClient(handler))
            .EnsureRuntimeAsync(options, progress: null, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("mihomo-binary", await File.ReadAllTextAsync(result.Value!));
        Assert.Equal([options.PrimaryArchiveUri, options.FallbackArchiveUri], handler.Requests);
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task EnsureRuntimeAsync_RejectsRedirectToUnapprovedHost()
    {
        var archive = CreateArchive(("mihomo-windows-amd64-compatible.exe", "mihomo-binary"));
        var options = CreateOptions(archive);
        var handler = new RuntimeHandler(request =>
        {
            var response = Response(request, archive);
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/mihomo.zip");
            return response;
        });

        var result = await new ProxyRuntimeDownloader(new HttpClient(handler))
            .EnsureRuntimeAsync(options, progress: null, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("proxy.runtime.origin", result.ErrorCode);
        Assert.False(File.Exists(Path.Combine(root, "mihomo.exe")));
    }

    [Fact]
    public async Task EnsureRuntimeAsync_DeletesArchiveWhenHashDoesNotMatch()
    {
        var archive = CreateArchive(("mihomo-windows-amd64-compatible.exe", "mihomo-binary"));
        var options = CreateOptions(archive) with { ExpectedSha256 = new string('0', 64) };

        var result = await new ProxyRuntimeDownloader(new HttpClient(new RuntimeHandler(request => Response(request, archive))))
            .EnsureRuntimeAsync(options, progress: null, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("proxy.runtime.hash", result.ErrorCode);
        Assert.False(File.Exists(Path.Combine(root, options.ArchiveFileName)));
        Assert.False(File.Exists(Path.Combine(root, options.ArchiveFileName + ".partial")));
    }

    [Fact]
    public async Task EnsureRuntimeAsync_RejectsArchiveContainingTraversalEntry()
    {
        var archive = CreateArchive(
            ("mihomo-windows-amd64-compatible.exe", "mihomo-binary"),
            ("../outside.exe", "unsafe"));
        var options = CreateOptions(archive);

        var result = await new ProxyRuntimeDownloader(new HttpClient(new RuntimeHandler(request => Response(request, archive))))
            .EnsureRuntimeAsync(options, progress: null, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("proxy.runtime.archive", result.ErrorCode);
        Assert.False(File.Exists(Path.Combine(root, "mihomo.exe")));
        Assert.False(File.Exists(Path.Combine(root, "..", "outside.exe")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private ProxyRuntimeOptions CreateOptions(byte[] archive) => new(
        Version: "v1.19.30",
        RuntimeDirectory: root,
        PrimaryArchiveUri: new Uri("https://www.qiuqiuqiu.top/xxx/codex-network/v1.19.30/mihomo.zip"),
        FallbackArchiveUri: new Uri("https://github.com/MetaCubeX/mihomo/releases/download/v1.19.30/mihomo.zip"),
        ExpectedSha256: Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant(),
        ArchiveFileName: "mihomo.zip",
        ArchiveEntryName: "mihomo-windows-amd64-compatible.exe");

    private static byte[] CreateArchive(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(item.Content);
            }
        }

        return buffer.ToArray();
    }

    private static HttpResponseMessage Response(HttpRequestMessage request, byte[] content) => new(HttpStatusCode.OK)
    {
        RequestMessage = request,
        Content = new ByteArrayContent(content)
    };

    private sealed class RuntimeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(handler(request));
        }
    }
}
