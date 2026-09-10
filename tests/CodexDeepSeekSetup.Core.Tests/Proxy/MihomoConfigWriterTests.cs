using CodexDeepSeekSetup.Core.Proxy;

namespace CodexDeepSeekSetup.Core.Tests.Proxy;

public sealed class MihomoConfigWriterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codex-mihomo-config-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Write_CreatesLoopbackOnlyNonTunConfigurationDeterministically()
    {
        var writer = new MihomoConfigWriter();

        var first = writer.Write(root, 17890);
        var content1 = await File.ReadAllTextAsync(first.Value!);
        var second = writer.Write(root, 17890);
        var content2 = await File.ReadAllTextAsync(second.Value!);

        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal(content1, content2);
        Assert.Contains("mixed-port: 17890", content1);
        Assert.Contains("allow-lan: false", content1);
        Assert.Contains("bind-address: 127.0.0.1", content1);
        Assert.Contains("enable: false", content1);
        Assert.Contains("path: ./providers/subscription.yaml", content1);
        Assert.Contains("MATCH,AUTO", content1);
        Assert.DoesNotContain("external-controller", content1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Write_RejectsInvalidPort(int port)
    {
        var result = new MihomoConfigWriter().Write(root, port);

        Assert.False(result.IsSuccess);
        Assert.Equal("proxy.config.port", result.ErrorCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
