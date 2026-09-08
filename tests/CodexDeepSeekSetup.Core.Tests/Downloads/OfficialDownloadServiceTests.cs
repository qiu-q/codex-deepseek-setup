using System.Net;
using System.Net.Http.Headers;
using CodexDeepSeekSetup.Core.Downloads;

namespace CodexDeepSeekSetup.Core.Tests.Downloads;

public sealed class OfficialDownloadServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codex-download-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("http://persistent.oaistatic.com/codex-app-prod/ChatGPT-x64.msix")]
    [InlineData("https://evil.example/ChatGPT-x64.msix")]
    [InlineData("https://persistent.oaistatic.com/other/file.msix")]
    public void EnsureAllowed_RejectsUnsafeOrUnknownLocations(string value)
    {
        Assert.False(new OfficialOriginPolicy().EnsureAllowed(new Uri(value)).IsSuccess);
    }

    [Theory]
    [InlineData(OfficialPayloadUrls.CodexMsix)]
    [InlineData(OfficialPayloadUrls.OfflineLicense)]
    public void EnsureAllowed_AcceptsOnlyOfficialPayloads(string value)
    {
        Assert.True(new OfficialOriginPolicy().EnsureAllowed(new Uri(value)).IsSuccess);
    }

    [Fact]
    public async Task DownloadCodexPayloadAsync_WritesBothFilesAndPromotesPartialFiles()
    {
        var handler = new PayloadHandler();
        var service = new OfficialDownloadService(new HttpClient(handler), new OfficialOriginPolicy());

        var result = await service.DownloadCodexPayloadAsync(root, progress: null, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("msix-content", await File.ReadAllTextAsync(result.Value!.MsixPath));
        Assert.Equal("license-content", await File.ReadAllTextAsync(result.Value.LicensePath));
        Assert.Empty(Directory.GetFiles(root, "*.partial"));
    }

    [Fact]
    public async Task DownloadCodexPayloadAsync_ResumesAnExistingPartialDownload()
    {
        Directory.CreateDirectory(root);
        var partial = Path.Combine(root, "ChatGPT-x64.msix.partial");
        await File.WriteAllTextAsync(partial, "msix-");
        var handler = new PayloadHandler();
        var service = new OfficialDownloadService(new HttpClient(handler), new OfficialOriginPolicy());

        var result = await service.DownloadCodexPayloadAsync(root, progress: null, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains(5, handler.RequestedRanges);
        Assert.Equal("msix-content", await File.ReadAllTextAsync(result.Value!.MsixPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class PayloadHandler : HttpMessageHandler
    {
        public List<long> RequestedRanges { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isMsix = request.RequestUri!.AbsolutePath.EndsWith(".msix", StringComparison.Ordinal);
            var full = isMsix ? "msix-content" : "license-content";
            var range = request.Headers.Range?.Ranges.SingleOrDefault()?.From;
            if (range is not null)
            {
                RequestedRanges.Add(range.Value);
            }

            var remaining = range is null ? full : full[(int)range.Value..];
            var response = new HttpResponseMessage(range is null ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            {
                RequestMessage = request,
                Content = new StringContent(remaining)
            };
            response.Content.Headers.ContentLength = remaining.Length;
            if (range is not null)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(range.Value, full.Length - 1, full.Length);
            }

            return Task.FromResult(response);
        }
    }
}
