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
        Assert.Contains("external-controller: 127.0.0.1:17891", content1);
        Assert.Contains("name: PROXY", content1);
        Assert.Contains("type: select", content1);
        Assert.Contains("MATCH,PROXY", content1);
        Assert.Contains("store-selected: true", content1);
        var secretPath = Path.Combine(root, MihomoConfigWriter.ControllerSecretFileName);
        Assert.True(File.Exists(secretPath));
        var secret = await File.ReadAllTextAsync(secretPath);
        Assert.True(secret.Length >= 32);
        Assert.Contains($"secret: '{secret}'", content1);
        Assert.Equal(secret, await File.ReadAllTextAsync(secretPath));
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

    [Fact]
    public async Task Write_ReplacesUnsafeControllerSecretBeforeWritingYaml()
    {
        Directory.CreateDirectory(root);
        var secretPath = Path.Combine(root, MihomoConfigWriter.ControllerSecretFileName);
        await File.WriteAllTextAsync(secretPath, "unsafe'\nallow-lan: true\nxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");

        var result = new MihomoConfigWriter().Write(root, 17890);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var secret = await File.ReadAllTextAsync(secretPath);
        Assert.Equal(32, secret.Length);
        Assert.All(secret, character => Assert.True(Uri.IsHexDigit(character)));
        Assert.DoesNotContain("allow-lan: true", await File.ReadAllTextAsync(result.Value!));
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
