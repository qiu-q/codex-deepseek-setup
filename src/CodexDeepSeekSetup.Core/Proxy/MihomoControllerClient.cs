using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Core.Proxy;

public sealed record ProxyNode(string Name, bool IsSelected);

public sealed class MihomoControllerClient
{
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly string secret;

    public MihomoControllerClient(HttpClient http, Uri endpoint, string secret)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        if (!endpoint.IsLoopback || endpoint.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("Mihomo 控制接口必须是本机 HTTP 地址。", nameof(endpoint));
        }

        this.http = http;
        this.endpoint = endpoint;
        this.secret = secret;
    }

    public async Task<OperationResult<IReadOnlyList<ProxyNode>>> GetNodesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, $"proxies/{MihomoConfigWriter.SelectorName}");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Failure<IReadOnlyList<ProxyNode>>("proxy.controller.nodes", "无法读取节点列表");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (!root.TryGetProperty("all", out var all) || all.ValueKind != JsonValueKind.Array)
            {
                return Failure<IReadOnlyList<ProxyNode>>("proxy.controller.nodes", "Mihomo 返回的节点列表无效");
            }

            var current = root.TryGetProperty("now", out var now) ? now.GetString() : null;
            var nodes = all.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                .Select(item => item.GetString()!)
                .Distinct(StringComparer.Ordinal)
                .Select(name => new ProxyNode(name, string.Equals(name, current, StringComparison.Ordinal)))
                .ToArray();
            return nodes.Length == 0
                ? Failure<IReadOnlyList<ProxyNode>>("proxy.controller.nodes", "订阅中没有可选择的节点")
                : OperationResult<IReadOnlyList<ProxyNode>>.Success(nodes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException)
        {
            return Failure<IReadOnlyList<ProxyNode>>("proxy.controller.nodes", "无法读取节点列表");
        }
    }

    public async Task<OperationResult<Unit>> SelectAsync(string nodeName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            return Failure<Unit>("proxy.controller.selection", "请选择一个节点");
        }

        try
        {
            using var request = CreateRequest(HttpMethod.Put, $"proxies/{MihomoConfigWriter.SelectorName}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, string> { ["name"] = nodeName }),
                Encoding.UTF8,
                "application/json");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? OperationResult<Unit>.Success(default)
                : Failure<Unit>("proxy.controller.selection", "Mihomo 未能应用所选节点");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Failure<Unit>("proxy.controller.selection", "无法连接本机 Mihomo 控制接口");
        }
    }

    public async Task<OperationResult<int>> GetDelayAsync(string nodeName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            return Failure<int>("proxy.controller.delay", "请选择一个节点");
        }

        try
        {
            var encoded = Uri.EscapeDataString(nodeName);
            const string probe = "https%3A%2F%2Fcp.cloudflare.com%2Fgenerate_204";
            using var request = CreateRequest(HttpMethod.Get, $"proxies/{encoded}/delay?timeout=5000&url={probe}");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Failure<int>("proxy.controller.delay", "节点延迟测试失败");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document.RootElement.TryGetProperty("delay", out var delay) && delay.TryGetInt32(out var milliseconds)
                ? OperationResult<int>.Success(milliseconds)
                : Failure<int>("proxy.controller.delay", "Mihomo 返回的延迟结果无效");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException)
        {
            return Failure<int>("proxy.controller.delay", "节点延迟测试失败");
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, new Uri(endpoint, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return request;
    }

    private static OperationResult<T> Failure<T>(string code, string message) =>
        OperationResult<T>.Failure(code, message);
}
