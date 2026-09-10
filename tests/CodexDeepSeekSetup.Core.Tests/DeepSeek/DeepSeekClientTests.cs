using System.Net;
using System.Text;
using CodexDeepSeekSetup.Core.DeepSeek;

namespace CodexDeepSeekSetup.Core.Tests.DeepSeek;

public sealed class DeepSeekClientTests
{
    [Fact]
    public async Task ValidateAsync_ReturnsBalancesForValidKey()
    {
        const string json = """
            {
              "is_available": true,
              "balance_infos": [
                {"currency":"CNY","total_balance":"12.50","granted_balance":"2.50","topped_up_balance":"10.00"}
              ]
            }
            """;
        var client = Create(HttpStatusCode.OK, json);

        var result = await client.ValidateAsync("sk-valid12345678", default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsAvailable);
        Assert.Equal("12.50", Assert.Single(result.Value.Balances).TotalBalance);
    }

    [Fact]
    public async Task ValidateAsync_MapsUnauthorizedWithoutEchoingKey()
    {
        const string key = "sk-secret12345678";
        var client = Create(HttpStatusCode.Unauthorized, "{}");

        var result = await client.ValidateAsync(key, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("deepseek.auth.invalid", result.ErrorCode);
        Assert.DoesNotContain(key, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.PaymentRequired, "deepseek.balance.insufficient")]
    [InlineData((HttpStatusCode)429, "deepseek.rate_limited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "deepseek.service.unavailable")]
    [InlineData(HttpStatusCode.NotFound, "deepseek.api.incompatible")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "deepseek.api.incompatible")]
    public async Task ValidateAsync_MapsKnownHttpFailures(HttpStatusCode status, string expectedCode)
    {
        var result = await Create(status, "{}").ValidateAsync("sk-valid12345678", default);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsMalformedSuccessPayload()
    {
        var result = await Create(HttpStatusCode.OK, "not-json")
            .ValidateAsync("sk-valid12345678", default);

        Assert.False(result.IsSuccess);
        Assert.Equal("deepseek.response.invalid", result.ErrorCode);
    }

    [Fact]
    public async Task TestResponseAsync_ReturnsOutputText()
    {
        const string json = """
            {
              "id":"resp_test",
              "status":"completed",
              "output":[
                {"type":"message","content":[{"type":"output_text","text":"连接成功"}]}
              ]
            }
            """;
        var client = Create(HttpStatusCode.OK, json);

        var result = await client.TestResponseAsync("sk-valid12345678", "deepseek-v4-flash", default);

        Assert.True(result.IsSuccess);
        Assert.Equal("连接成功", result.Value);
    }

    [Fact]
    public async Task ValidateAsync_RejectsFinalResponseFromAnotherHost()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/user/balance")
        };
        var client = new DeepSeekClient(new HttpClient(new FixedFinalResponseHandler(response)));

        var result = await client.ValidateAsync("sk-valid12345678", default);

        Assert.False(result.IsSuccess);
        Assert.Equal("deepseek.origin.rejected", result.ErrorCode);
    }

    private static DeepSeekClient Create(HttpStatusCode status, string content)
    {
        var handler = new StaticResponseHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        });
        return new DeepSeekClient(new HttpClient(handler));
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class FixedFinalResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
