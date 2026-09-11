using System.Net;
using System.Text;
using System.Text.Json;
using CodexDeepSeekSetup.Core.Proxy;

namespace CodexDeepSeekSetup.Core.Tests.Proxy;

public sealed class MihomoControllerClientTests
{
    [Fact]
    public async Task GetNodesAsync_ReturnsProviderNodesAndCurrentSelection()
    {
        var handler = new RecordingHandler(request => Json(request,
            """{"type":"Selector","now":"日本 01","all":["AUTO","日本 01","新加坡 02"]}"""));
        var client = new MihomoControllerClient(
            new HttpClient(handler),
            new Uri("http://127.0.0.1:17891/"),
            "controller-secret");

        var result = await client.GetNodesAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(["AUTO", "日本 01", "新加坡 02"], result.Value!.Select(node => node.Name));
        Assert.Equal("日本 01", result.Value!.Single(node => node.IsSelected).Name);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("controller-secret", handler.LastRequest.Headers.Authorization?.Parameter);
        Assert.Equal("/proxies/PROXY", handler.LastRequest.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task SelectAsync_EncodesNodeNameAndSendsSelection()
    {
        var handler = new RecordingHandler(request =>
            new HttpResponseMessage(HttpStatusCode.NoContent) { RequestMessage = request });
        var client = new MihomoControllerClient(
            new HttpClient(handler),
            new Uri("http://127.0.0.1:17891/"),
            "controller-secret");

        var result = await client.SelectAsync("日本 01", default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Equal("/proxies/PROXY", handler.LastRequest.RequestUri?.AbsolutePath);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("日本 01", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetDelayAsync_ReturnsMeasuredDelayForEncodedNode()
    {
        var handler = new RecordingHandler(request => Json(request, """{"delay":61}"""));
        var client = new MihomoControllerClient(
            new HttpClient(handler),
            new Uri("http://127.0.0.1:17891/"),
            "controller-secret");

        var result = await client.GetDelayAsync("日本 01", default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(61, result.Value);
        Assert.Equal("/proxies/%E6%97%A5%E6%9C%AC%2001/delay", handler.LastRequest!.RequestUri?.AbsolutePath);
        Assert.Contains("timeout=5000", handler.LastRequest.RequestUri?.Query);
    }

    private static HttpResponseMessage Json(HttpRequestMessage request, string json) => new(HttpStatusCode.OK)
    {
        RequestMessage = request,
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
