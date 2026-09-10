using System.Net;
using System.Text;
using CodexDeepSeekSetup.Core.Guides;

namespace CodexDeepSeekSetup.Core.Tests.Guides;

public sealed class GuideClientTests
{
    private static readonly Uri Endpoint = new("https://www.qiuqiuqiu.top/xxx/codex-ad/");

    [Fact]
    public async Task GetCurrentAsync_ReturnsCompleteValidatedGuideInServerOrder()
    {
        var client = Client(HttpStatusCode.OK, """
            {
              "version": 1,
              "enabled": true,
              "title": "DeepSeek API 使用准备",
              "steps": [
                {
                  "id": "sign-in",
                  "title": "登录或注册",
                  "body": "使用手机号验证码登录。",
                  "completionHint": "已进入控制台",
                  "actionText": "打开登录页",
                  "actionUrl": "https://platform.deepseek.com/sign_in",
                  "imageUrl": "https://www.qiuqiuqiu.top/xxx/codex-ad/media/login.webp"
                },
                {
                  "id": "api-key",
                  "title": "创建 API Key",
                  "body": "创建后立即复制。",
                  "completionHint": "已经复制 sk- 开头的 Key",
                  "actionText": "打开 API Keys",
                  "actionUrl": "https://platform.deepseek.com/api_keys",
                  "imageUrl": null
                }
              ]
            }
            """);

        var guide = await client.GetCurrentAsync(default);

        Assert.NotNull(guide);
        Assert.Equal("DeepSeek API 使用准备", guide.Title);
        Assert.Equal(["sign-in", "api-key"], guide.Steps.Select(step => step.Id));
        Assert.Equal("https://platform.deepseek.com/sign_in", guide.Steps[0].ActionUrl.AbsoluteUri);
        Assert.Equal("https://www.qiuqiuqiu.top/xxx/codex-ad/media/login.webp", guide.Steps[0].ImageUrl?.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, "")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    [InlineData(HttpStatusCode.OK, "{\"version\":1,\"enabled\":false,\"title\":\"guide\",\"steps\":[]}")]
    [InlineData(HttpStatusCode.OK, "{\"version\":1,\"enabled\":true,\"title\":\"guide\",\"steps\":[]}")]
    [InlineData(HttpStatusCode.OK, "{\"version\":1,\"enabled\":true,\"title\":\"guide\",\"steps\":[{\"id\":\"one\",\"title\":\"t\",\"body\":\"b\",\"completionHint\":\"done\",\"actionText\":\"go\",\"actionUrl\":\"http://example.com\"}]}")]
    [InlineData(HttpStatusCode.OK, "{\"version\":1,\"enabled\":true,\"title\":\"guide\",\"steps\":[{\"id\":\"one\",\"title\":\"t\",\"body\":\"b\",\"completionHint\":\"done\",\"actionText\":\"go\",\"actionUrl\":\"https://example.com\",\"imageUrl\":\"http://example.com/a.png\"}]}")]
    public async Task GetCurrentAsync_RejectsUnavailableOrUnsafeDocuments(HttpStatusCode status, string body)
    {
        Assert.Null(await Client(status, body).GetCurrentAsync(default));
    }

    [Fact]
    public async Task GetCurrentAsync_RejectsDuplicateStepIdsAsAWhole()
    {
        var body = Document(Step("same"), Step("same"));

        Assert.Null(await Client(HttpStatusCode.OK, body).GetCurrentAsync(default));
    }

    [Fact]
    public async Task GetCurrentAsync_RejectsMoreThanEightSteps()
    {
        var body = Document(Enumerable.Range(1, 9).Select(index => Step($"step-{index}")).ToArray());

        Assert.Null(await Client(HttpStatusCode.OK, body).GetCurrentAsync(default));
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsNullWhenItsThreeSecondRequestBudgetExpires()
    {
        var handler = new DelayedHandler();
        var client = new GuideClient(new HttpClient(handler), Endpoint, TimeSpan.FromMilliseconds(20));

        var guide = await client.GetCurrentAsync(default);

        Assert.Null(guide);
        Assert.True(handler.WasCancelled);
    }

    private static GuideClient Client(HttpStatusCode status, string body) =>
        new(new HttpClient(new StaticHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        })), Endpoint);

    private static string Document(params string[] steps) =>
        $$"""{"version":1,"enabled":true,"title":"Guide","steps":[{{string.Join(',', steps)}}]}""";

    private static string Step(string id) =>
        $$"""{"id":"{{id}}","title":"Title","body":"Body","completionHint":"Done","actionText":"Open","actionUrl":"https://example.com/{{id}}"}""";

    private sealed class StaticHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        public bool WasCancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }
    }
}
