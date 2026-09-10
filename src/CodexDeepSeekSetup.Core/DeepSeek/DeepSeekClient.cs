using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Core.DeepSeek;

public sealed class DeepSeekClient
{
    private static readonly Uri OfficialBaseUri = new("https://api.deepseek.com/");
    private readonly HttpClient http;

    public DeepSeekClient(HttpClient http)
    {
        this.http = http;
        this.http.BaseAddress ??= OfficialBaseUri;
    }

    public async Task<OperationResult<DeepSeekAccountStatus>> ValidateAsync(
        string key,
        CancellationToken cancellationToken)
    {
        if (!IsPlausibleKey(key))
        {
            return OperationResult<DeepSeekAccountStatus>.Failure(
                "deepseek.auth.invalid",
                "API Key 格式无效，应以 sk- 开头");
        }

        try
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Get, "user/balance", key);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!IsOfficialResponse(response))
            {
                return OperationResult<DeepSeekAccountStatus>.Failure("deepseek.origin.rejected", "DeepSeek 请求被重定向到非官方地址，已停止");
            }
            var mapped = MapFailure<DeepSeekAccountStatus>(response.StatusCode);
            if (mapped is not null)
            {
                return mapped;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<BalanceResponse>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (payload is null)
            {
                return InvalidResponse<DeepSeekAccountStatus>();
            }

            var balances = payload.BalanceInfos
                .Select(item => new DeepSeekBalance(
                    item.Currency,
                    item.TotalBalance,
                    item.GrantedBalance,
                    item.ToppedUpBalance))
                .ToArray();
            return OperationResult<DeepSeekAccountStatus>.Success(
                new DeepSeekAccountStatus(payload.IsAvailable, balances));
        }
        catch (JsonException)
        {
            return InvalidResponse<DeepSeekAccountStatus>();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperationResult<DeepSeekAccountStatus>.Failure("deepseek.network.timeout", "连接 DeepSeek 超时");
        }
        catch (HttpRequestException)
        {
            return OperationResult<DeepSeekAccountStatus>.Failure("deepseek.network.failed", "无法连接 DeepSeek 官方 API");
        }
    }

    public async Task<OperationResult<string>> TestResponseAsync(
        string key,
        string model,
        CancellationToken cancellationToken)
    {
        if (!IsPlausibleKey(key))
        {
            return OperationResult<string>.Failure("deepseek.auth.invalid", "API Key 格式无效，应以 sk- 开头");
        }

        try
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Post, "responses", key);
            request.Content = JsonContent.Create(new
            {
                model,
                input = "只回复：连接成功",
                reasoning = new { effort = "none" },
                stream = false,
                max_output_tokens = 32
            });
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!IsOfficialResponse(response))
            {
                return OperationResult<string>.Failure("deepseek.origin.rejected", "DeepSeek 请求被重定向到非官方地址，已停止");
            }
            var mapped = MapFailure<string>(response.StatusCode);
            if (mapped is not null)
            {
                return mapped;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<ResponseEnvelope>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var text = payload?.Output
                .Where(item => item.Type == "message")
                .SelectMany(item => item.Content)
                .FirstOrDefault(part => part.Type == "output_text")
                ?.Text;
            return string.IsNullOrWhiteSpace(text)
                ? InvalidResponse<string>()
                : OperationResult<string>.Success(text);
        }
        catch (JsonException)
        {
            return InvalidResponse<string>();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperationResult<string>.Failure("deepseek.network.timeout", "连接 DeepSeek 超时");
        }
        catch (HttpRequestException)
        {
            return OperationResult<string>.Failure("deepseek.network.failed", "无法连接 DeepSeek 官方 API");
        }
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string path, string key)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    private static OperationResult<T>? MapFailure<T>(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.OK => null,
            HttpStatusCode.Unauthorized => OperationResult<T>.Failure("deepseek.auth.invalid", "API Key 无效"),
            HttpStatusCode.PaymentRequired => OperationResult<T>.Failure("deepseek.balance.insufficient", "账户余额不足"),
            HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity =>
                OperationResult<T>.Failure("deepseek.api.incompatible", "DeepSeek API 接口或模型暂不兼容，请检查官方服务状态和模型配置"),
            (HttpStatusCode)429 => OperationResult<T>.Failure("deepseek.rate_limited", "请求过于频繁，请稍后重试"),
            >= HttpStatusCode.InternalServerError => OperationResult<T>.Failure("deepseek.service.unavailable", "DeepSeek 服务暂时不可用"),
            _ => OperationResult<T>.Failure("deepseek.request.failed", $"DeepSeek 请求失败（HTTP {(int)statusCode}）")
        };
    }

    private static OperationResult<T> InvalidResponse<T>() =>
        OperationResult<T>.Failure("deepseek.response.invalid", "DeepSeek 返回了无法识别的数据");

    private static bool IsPlausibleKey(string key) =>
        key.StartsWith("sk-", StringComparison.Ordinal) && key.Length >= 11;

    private static bool IsOfficialResponse(HttpResponseMessage response)
    {
        var uri = response.RequestMessage?.RequestUri;
        return uri is not null &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(uri.Host, OfficialBaseUri.Host, StringComparison.OrdinalIgnoreCase);
    }
}
