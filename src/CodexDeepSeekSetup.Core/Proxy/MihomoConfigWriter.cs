using System.Text;
using System.Security.Cryptography;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Core.Proxy;

public sealed class MihomoConfigWriter
{
    public const string ControllerSecretFileName = "controller-secret";
    public const int ControllerPort = 17891;
    public const string SelectorName = "PROXY";

    public OperationResult<string> Write(string dataDirectory, int mixedPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (mixedPort is < 1 or > 65535)
        {
            return OperationResult<string>.Failure("proxy.config.port", "代理端口无效");
        }

        try
        {
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(Path.Combine(dataDirectory, "providers"));
            var secretPath = Path.Combine(dataDirectory, ControllerSecretFileName);
            var controllerSecret = ReadOrCreateControllerSecret(secretPath);
            var path = Path.Combine(dataDirectory, "config.yaml");
            var partialPath = path + ".partial";
            File.WriteAllText(partialPath, BuildConfiguration(mixedPort, controllerSecret), new UTF8Encoding(false));
            File.Move(partialPath, path, overwrite: true);
            return OperationResult<string>.Success(path);
        }
        catch (IOException)
        {
            return OperationResult<string>.Failure("proxy.config.file", "无法写入 Mihomo 配置文件");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult<string>.Failure("proxy.config.access", "没有权限写入 Mihomo 配置文件");
        }
    }

    private static string ReadOrCreateControllerSecret(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length == 32 && existing.All(Uri.IsHexDigit))
            {
                return existing;
            }
        }

        var created = RandomNumberGenerator.GetHexString(32).ToLowerInvariant();
        File.WriteAllText(path, created, new UTF8Encoding(false));
        return created;
    }

    private static string BuildConfiguration(int mixedPort, string controllerSecret) => $$"""
        mixed-port: {{mixedPort}}
        allow-lan: false
        bind-address: 127.0.0.1
        external-controller: 127.0.0.1:{{ControllerPort}}
        secret: '{{controllerSecret}}'
        mode: rule
        log-level: warning
        ipv6: false
        profile:
          store-selected: true
        tun:
          enable: false
        proxy-providers:
          subscription:
            type: file
            path: ./providers/subscription.yaml
            health-check:
              enable: true
              url: https://cp.cloudflare.com
              interval: 300
              timeout: 5000
        proxy-groups:
          - name: AUTO
            type: url-test
            use:
              - subscription
            url: https://cp.cloudflare.com
            interval: 300
          - name: {{SelectorName}}
            type: select
            proxies:
              - AUTO
            use:
              - subscription
        rules:
          - IP-CIDR,127.0.0.0/8,DIRECT,no-resolve
          - IP-CIDR,10.0.0.0/8,DIRECT,no-resolve
          - IP-CIDR,172.16.0.0/12,DIRECT,no-resolve
          - IP-CIDR,192.168.0.0/16,DIRECT,no-resolve
          - MATCH,{{SelectorName}}
        """ + Environment.NewLine;
}
