using System.Net;
using System.Net.Http.Headers;
using CodexDeepSeekSetup.Core.Proxy;

namespace CodexDeepSeekSetup.Core.Tests.Proxy;

public sealed class SubscriptionImporterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codex-subscription-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ImportAsync_RejectsNonHttpsWithoutSendingRequest()
    {
        var handler = new SubscriptionHandler(_ => throw new InvalidOperationException("must not send"));
        var destination = Path.Combine(root, "subscription.yaml");

        var result = await new SubscriptionImporter(new HttpClient(handler))
            .ImportAsync(new Uri("http://example.com/sub"), destination, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("proxy.subscription.url", result.ErrorCode);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ImportAsync_PreservesPreviousProviderWhenResponseIsHtml()
    {
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "subscription.yaml");
        await File.WriteAllTextAsync(destination, "previous-provider");
        var handler = new SubscriptionHandler(request => Response(request, "<!doctype html><title>error</title>", "text/html"));

        var result = await new SubscriptionImporter(new HttpClient(handler))
            .ImportAsync(new Uri("https://provider.example/sub"), destination, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("proxy.subscription.content", result.ErrorCode);
        Assert.Equal("previous-provider", await File.ReadAllTextAsync(destination));
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task ImportAsync_RejectsPayloadLargerThanLimit()
    {
        var bytes = new byte[SubscriptionImporter.MaximumBytes + 1];
        var handler = new SubscriptionHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(bytes)
            };
            response.Content.Headers.ContentLength = bytes.Length;
            return response;
        });

        var result = await new SubscriptionImporter(new HttpClient(handler))
            .ImportAsync(new Uri("https://provider.example/sub"), Path.Combine(root, "subscription.yaml"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("proxy.subscription.size", result.ErrorCode);
    }

    [Fact]
    public async Task ImportAsync_AtomicallySavesSupportedProviderContent()
    {
        var destination = Path.Combine(root, "providers", "subscription.yaml");
        var handler = new SubscriptionHandler(request => Response(request, "proxies:\n- name: test\n  type: ss\n", "text/yaml"));

        var result = await new SubscriptionImporter(new HttpClient(handler))
            .ImportAsync(new Uri("https://provider.example/sub"), destination, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("name: test", await File.ReadAllTextAsync(destination));
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task ImportAsync_RequestsClashCompatibleYaml()
    {
        string? userAgent = null;
        var handler = new SubscriptionHandler(request =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            return Response(request, "proxies: []\n", "text/yaml");
        });

        var result = await new SubscriptionImporter(new HttpClient(handler))
            .ImportAsync(new Uri("https://provider.example/sub"), Path.Combine(root, "subscription.yaml"), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("clash.meta", userAgent);
    }

    [Fact]
    public async Task ImportAsync_AcceptsClashYamlWhenProviderMislabelsItAsHtml()
    {
        var destination = Path.Combine(root, "providers", "subscription.yaml");
        var handler = new SubscriptionHandler(request => Response(
            request,
            "proxies:\n- name: mislabeled-provider\n  type: ss\n",
            "text/html"));

        var result = await new SubscriptionImporter(new HttpClient(handler))
            .ImportAsync(new Uri("https://provider.example/sub"), destination, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("mislabeled-provider", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task ImportAsync_PreservesSafeNetworkFailureDetailsForDiagnostics()
    {
        var handler = new SubscriptionHandler(_ => throw new HttpRequestException(
            "TLS handshake failed for https://provider.example/sub?token=secret-value"));

        var result = await new SubscriptionImporter(new HttpClient(handler))
            .ImportAsync(new Uri("https://provider.example/sub?token=another-secret"), Path.Combine(root, "subscription.yaml"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("proxy.subscription.network", result.ErrorCode);
        Assert.Contains("HttpRequestException", result.DiagnosticDetails, StringComparison.Ordinal);
        Assert.Contains("TLS handshake failed", result.DiagnosticDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", result.DiagnosticDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("another-secret", result.DiagnosticDetails, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static HttpResponseMessage Response(HttpRequestMessage request, string content, string mediaType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(content)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }

    private sealed class SubscriptionHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(handler(request));
        }
    }
}
