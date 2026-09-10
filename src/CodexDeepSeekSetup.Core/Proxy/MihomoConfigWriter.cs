using System.Text;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Core.Proxy;

public sealed class MihomoConfigWriter
{
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
            var path = Path.Combine(dataDirectory, "config.yaml");
            var partialPath = path + ".partial";
            File.WriteAllText(partialPath, BuildConfiguration(mixedPort), new UTF8Encoding(false));
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

    private static string BuildConfiguration(int mixedPort) => $$"""
        mixed-port: {{mixedPort}}
        allow-lan: false
        bind-address: 127.0.0.1
        mode: rule
        log-level: warning
        ipv6: false
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
        rules:
          - IP-CIDR,127.0.0.0/8,DIRECT,no-resolve
          - IP-CIDR,10.0.0.0/8,DIRECT,no-resolve
          - IP-CIDR,172.16.0.0/12,DIRECT,no-resolve
          - IP-CIDR,192.168.0.0/16,DIRECT,no-resolve
          - MATCH,AUTO
        """ + Environment.NewLine;
}
