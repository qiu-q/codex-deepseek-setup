using System.Net;
using System.Text;
using CodexDeepSeekSetup.Core.Advertisements;

namespace CodexDeepSeekSetup.Core.Tests.Advertisements;

public sealed class AdvertisementClientTests
{
    private static readonly Uri Endpoint = new("https://www.qiuqiuqiu.top/xxx/codex-ad/");

    [Fact]
    public async Task GetCurrentAsync_ReturnsValidatedActiveCampaign()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {
              "version": 1,
              "enabled": true,
              "campaignId": "autumn-01",
              "title": "推广标题",
              "body": "推广说明",
              "ctaText": "了解详情",
              "targetUrl": "https://example.com/product",
              "imageUrl": "https://www.qiuqiuqiu.top/xxx/codex-ad/media/banner.webp",
              "startsAt": "2026-09-01T00:00:00+08:00",
              "endsAt": "2026-10-01T00:00:00+08:00"
            }
            """));
        var client = new AdvertisementClient(new HttpClient(handler), Endpoint, () => DateTimeOffset.Parse("2026-09-10T12:00:00+08:00"));

        var campaign = await client.GetCurrentAsync(default);

        Assert.NotNull(campaign);
        Assert.Equal("autumn-01", campaign.CampaignId);
        Assert.Equal("https://example.com/product", campaign.TargetUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, "")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    [InlineData(HttpStatusCode.OK, "{\"version\":1,\"enabled\":true,\"campaignId\":\"x\",\"title\":\"t\",\"body\":\"b\",\"ctaText\":\"go\",\"targetUrl\":\"http://example.com\"}")]
    [InlineData(HttpStatusCode.OK, "{\"version\":1,\"enabled\":false,\"campaignId\":\"x\",\"title\":\"t\",\"body\":\"b\",\"ctaText\":\"go\",\"targetUrl\":\"https://example.com\"}")]
    public async Task GetCurrentAsync_HidesUnavailableOrUnsafeCampaign(HttpStatusCode status, string body)
    {
        var client = new AdvertisementClient(new HttpClient(new RecordingHandler(_ => Json(status, body))), Endpoint);

        var campaign = await client.GetCurrentAsync(default);

        Assert.Null(campaign);
    }

    [Fact]
    public async Task GetCurrentAsync_HidesCampaignOutsideSchedule()
    {
        var response = """
            {"version":1,"enabled":true,"campaignId":"ended","title":"t","body":"b","ctaText":"go","targetUrl":"https://example.com","endsAt":"2026-09-01T00:00:00Z"}
            """;
        var client = new AdvertisementClient(
            new HttpClient(new RecordingHandler(_ => Json(HttpStatusCode.OK, response))),
            Endpoint,
            () => DateTimeOffset.Parse("2026-09-10T00:00:00Z"));

        Assert.Null(await client.GetCurrentAsync(default));
    }

    [Fact]
    public async Task TrackAsync_SendsOneImpressionWithoutDeviceIdentity()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Accepted, ""));
        var client = new AdvertisementClient(new HttpClient(handler), Endpoint);

        await client.TrackAsync("autumn-01", AdEventType.Impression, default);
        await client.TrackAsync("autumn-01", AdEventType.Impression, default);

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("api/v1/events", request.UriPath, StringComparison.Ordinal);
        Assert.Contains("\"campaignId\":\"autumn-01\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"event\":\"impression\"", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("device", request.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user", request.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImageDownloader_AcceptsSmallSupportedImage()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x89, 0x50, 0x4e, 0x47])
        };
        response.Content.Headers.ContentType = new("image/png");
        var downloader = new AdvertisementImageDownloader(
            new HttpClient(new RecordingHandler(_ => response)));

        var bytes = await downloader.DownloadAsync(new Uri("https://example.com/banner.png"), default);

        Assert.Equal([0x89, 0x50, 0x4e, 0x47], bytes);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    public async Task ImageDownloader_RejectsUnsupportedContentType(string contentType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        response.Content.Headers.ContentType = new(contentType);
        var downloader = new AdvertisementImageDownloader(new HttpClient(new RecordingHandler(_ => response)));

        Assert.Null(await downloader.DownloadAsync(new Uri("https://example.com/banner"), default));
    }

    [Fact]
    public async Task ImageDownloader_RejectsPayloadOverTwoMegabytes()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[2 * 1024 * 1024 + 1])
        };
        response.Content.Headers.ContentType = new("image/webp");
        var downloader = new AdvertisementImageDownloader(new HttpClient(new RecordingHandler(_ => response)));

        Assert.Null(await downloader.DownloadAsync(new Uri("https://example.com/banner.webp"), default));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri?.PathAndQuery.TrimStart('/') ?? string.Empty,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            var response = responseFactory(request);
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed record CapturedRequest(string UriPath, string Body);
}
