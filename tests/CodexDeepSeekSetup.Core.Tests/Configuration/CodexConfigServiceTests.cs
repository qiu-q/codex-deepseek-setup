using CodexDeepSeekSetup.Core.Configuration;
using Tomlyn;
using Tomlyn.Model;

namespace CodexDeepSeekSetup.Core.Tests.Configuration;

public sealed class CodexConfigServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codex-config-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplyAsync_PreservesPluginsAndMcpWhileReplacingProvider()
    {
        Directory.CreateDirectory(root);
        const string original = """
            model = "old-model"

            [plugins."browser@openai-bundled"]
            enabled = true

            [mcp_servers.demo]
            command = "demo.exe"
            """;
        await File.WriteAllTextAsync(Path.Combine(root, "config.toml"), original);
        var service = new CodexConfigService();

        var result = await service.ApplyAsync(CreateRequest(), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var mergedText = await File.ReadAllTextAsync(Path.Combine(root, "config.toml"));
        var merged = TomlSerializer.Deserialize<TomlTable>(mergedText)!;
        Assert.Equal("deepseek-v4-flash", merged["model"]);
        Assert.Equal("deepseek", merged["model_provider"]);
        Assert.True((bool)((TomlTable)((TomlTable)merged["plugins"]!)["browser@openai-bundled"]!)["enabled"]!);
        Assert.Equal("demo.exe", ((TomlTable)((TomlTable)merged["mcp_servers"]!)["demo"]!)["command"]);
        Assert.DoesNotContain("sk-", mergedText, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(result.Value!.BackupDirectory, "config.toml")));
    }

    [Fact]
    public async Task ApplyAsync_WritesCatalogAndCredentialCommandWithoutAKey()
    {
        var result = await new CodexConfigService().ApplyAsync(CreateRequest(), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var configText = await File.ReadAllTextAsync(result.Value!.ConfigPath);
        var catalogText = await File.ReadAllTextAsync(result.Value.ModelCatalogPath);
        var config = TomlSerializer.Deserialize<TomlTable>(configText)!;
        var providers = (TomlTable)config["model_providers"]!;
        var deepSeek = (TomlTable)providers["deepseek"]!;
        var auth = (TomlTable)deepSeek["auth"]!;
        Assert.Equal(@"C:\Program Files\Codex Setup\CodexDeepSeekSetup.exe", auth["command"]);
        Assert.Contains("deepseek-v4-flash", catalogText);
        Assert.DoesNotContain("sk-", configText + catalogText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_LeavesMalformedExistingConfigUntouched()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.toml");
        const string malformed = "notify = [\"unterminated\"";
        await File.WriteAllTextAsync(path, malformed);

        var result = await new CodexConfigService().ApplyAsync(CreateRequest(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("config.toml.invalid", result.ErrorCode);
        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    private CodexConfigRequest CreateRequest() => new(
        root,
        "deepseek-v4-flash",
        @"C:\Program Files\Codex Setup\CodexDeepSeekSetup.exe",
        "CodexDeepSeekSetup/DeepSeekApiKey");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
