using CodexDeepSeekSetup.Windows.Cleanup;
using CodexDeepSeekSetup.Windows.Packages;

namespace CodexDeepSeekSetup.Windows.Tests.Cleanup;

public sealed class CodexArtifactInventoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codex-inventory-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ScanAsync_ReturnsOnlyExistingKnownCodexLocationsWithSizes()
    {
        var roots = CreateRoots();
        Directory.CreateDirectory(roots.CodexHome);
        await File.WriteAllBytesAsync(Path.Combine(roots.CodexHome, "state.db"), new byte[17]);
        var unrelated = Path.Combine(roots.UserProfile, "unrelated");
        Directory.CreateDirectory(unrelated);
        await File.WriteAllBytesAsync(Path.Combine(unrelated, "keep.bin"), new byte[99]);
        var inventory = new CodexArtifactInventory(roots);

        var items = await inventory.ScanAsync(
            Path.Combine(root, "downloads"),
            CodexPackageStatus.NotInstalled,
            credentialExists: false,
            codexCliPath: null,
            default);

        var codexHome = Assert.Single(items.Where(item => item.Id == CodexArtifactIds.CodexHome));
        Assert.Equal(17, codexHome.SizeBytes);
        Assert.DoesNotContain(items, item => item.Location.Contains("unrelated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_ListsOnlyKnownDownloadFiles()
    {
        var roots = CreateRoots();
        var downloads = Path.Combine(root, "downloads");
        Directory.CreateDirectory(downloads);
        await File.WriteAllBytesAsync(Path.Combine(downloads, "ChatGPT-x64.msix"), new byte[11]);
        await File.WriteAllBytesAsync(Path.Combine(downloads, "ChatGPT-License.xml"), new byte[7]);
        await File.WriteAllBytesAsync(Path.Combine(downloads, "customer-file.zip"), new byte[31]);
        var inventory = new CodexArtifactInventory(roots);

        var items = await inventory.ScanAsync(
            downloads,
            CodexPackageStatus.NotInstalled,
            credentialExists: false,
            codexCliPath: null,
            default);

        var payload = Assert.Single(items.Where(item => item.Id == CodexArtifactIds.Downloads));
        Assert.Equal(18, payload.SizeBytes);
    }

    [Fact]
    public async Task ScanAsync_IncludesOfficialDesktopRuntimeDirectory()
    {
        var roots = CreateRoots();
        var runtime = Path.Combine(roots.LocalAppData, "OpenAI", "Codex");
        Directory.CreateDirectory(runtime);
        await File.WriteAllBytesAsync(Path.Combine(runtime, "runtime.bin"), new byte[23]);
        var inventory = new CodexArtifactInventory(roots);

        var items = await inventory.ScanAsync(
            Path.Combine(root, "downloads"),
            CodexPackageStatus.NotInstalled,
            credentialExists: false,
            codexCliPath: null,
            default);

        var item = Assert.Single(items.Where(candidate => candidate.Id == CodexArtifactIds.DesktopRuntime));
        Assert.Equal(23, item.SizeBytes);
        Assert.True(item.IsDestructiveUserData);
    }

    [Fact]
    public async Task ScanAsync_UsesSafeEffectiveCodexHome()
    {
        var roots = CreateRoots();
        var customHome = Path.Combine(roots.UserProfile, "CustomCodexHome");
        Directory.CreateDirectory(customHome);
        await File.WriteAllBytesAsync(Path.Combine(customHome, "config.toml"), new byte[13]);
        var inventory = new CodexArtifactInventory(roots);

        var items = await inventory.ScanAsync(
            Path.Combine(root, "downloads"),
            CodexPackageStatus.NotInstalled,
            credentialExists: false,
            codexCliPath: null,
            effectiveCodexHome: customHome,
            default);

        var item = Assert.Single(items.Where(candidate => candidate.Id == CodexArtifactIds.CodexHome));
        Assert.Equal(customHome, item.Location);
        Assert.Equal(13, item.SizeBytes);
    }

    [Fact]
    public async Task ScanAsync_ReportsUnsafeExternalCodexHomeWithoutReadingIt()
    {
        var roots = CreateRoots();
        var inventory = new CodexArtifactInventory(roots);

        var items = await inventory.ScanAsync(
            Path.Combine(root, "downloads"),
            CodexPackageStatus.NotInstalled,
            credentialExists: false,
            codexCliPath: null,
            effectiveCodexHome: Path.GetPathRoot(roots.UserProfile),
            default);

        var item = Assert.Single(items.Where(candidate => candidate.Id == CodexArtifactIds.ExternalCodexHome));
        Assert.Null(item.SizeBytes);
        Assert.False(item.DefaultSelected);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private CleanupRoots CreateRoots()
    {
        var profile = Path.Combine(root, "profile");
        var local = Path.Combine(profile, "AppData", "Local");
        var publicDocs = Path.Combine(root, "public", "Documents");
        return new CleanupRoots(
            profile,
            local,
            Path.Combine(profile, "Desktop"),
            Path.Combine(profile, ".codex"),
            Path.Combine(local, "CodexDeepSeekSetup"),
            Path.Combine(local, "Programs", "CodexDeepSeekSetup"),
            Path.Combine(local, "Programs", "OpenAI", "Codex"),
            Path.Combine(local, "Programs", "OpenAI", "CodexPortable"),
            publicDocs,
            Path.Combine(publicDocs, "CodexDeepSeekSetup"));
    }
}
