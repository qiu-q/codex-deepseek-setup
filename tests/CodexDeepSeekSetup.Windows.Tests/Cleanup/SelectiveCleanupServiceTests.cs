using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Cleanup;
using CodexDeepSeekSetup.Windows.Security;

namespace CodexDeepSeekSetup.Windows.Tests.Cleanup;

public sealed class SelectiveCleanupServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codex-selective-cleanup-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CleanAsync_DeletesOnlySelectedKnownRoot()
    {
        var roots = CreateRoots();
        Directory.CreateDirectory(roots.CodexHome);
        Directory.CreateDirectory(roots.CliRoot);
        await File.WriteAllTextAsync(Path.Combine(roots.CodexHome, "remove.txt"), "x");
        await File.WriteAllTextAsync(Path.Combine(roots.CliRoot, "keep.txt"), "x");
        var service = new SelectiveCleanupService(new EmptySecretStore(), new MemoryEnvironment(), roots);

        var result = await service.CleanAsync(
            [CodexArtifactIds.CodexHome],
            Path.Combine(root, "downloads"),
            previousCodexCliPath: null,
            default);

        Assert.True(result.IsSuccess);
        Assert.False(Directory.Exists(roots.CodexHome));
        Assert.True(Directory.Exists(roots.CliRoot));
    }

    [Fact]
    public async Task CleanAsync_DeletesOnlyKnownPayloadNamesFromChosenDirectory()
    {
        var roots = CreateRoots();
        var downloads = Path.Combine(root, "downloads");
        Directory.CreateDirectory(downloads);
        await File.WriteAllTextAsync(Path.Combine(downloads, "ChatGPT-x64.msix"), "remove");
        await File.WriteAllTextAsync(Path.Combine(downloads, "ChatGPT-License.xml.part"), "remove");
        await File.WriteAllTextAsync(Path.Combine(downloads, "customer-file.zip"), "keep");
        var service = new SelectiveCleanupService(new EmptySecretStore(), new MemoryEnvironment(), roots);

        var result = await service.CleanAsync(
            [CodexArtifactIds.Downloads],
            downloads,
            previousCodexCliPath: null,
            default);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(Path.Combine(downloads, "ChatGPT-x64.msix")));
        Assert.False(File.Exists(Path.Combine(downloads, "ChatGPT-License.xml.part")));
        Assert.True(File.Exists(Path.Combine(downloads, "customer-file.zip")));
    }

    [Fact]
    public async Task CleanAsync_AssistantStateDoesNotDeleteUncheckedFallbackDownloadsOrUnknownFiles()
    {
        var roots = CreateRoots();
        var downloads = Path.Combine(roots.AssistantDataRoot, "Downloads");
        Directory.CreateDirectory(downloads);
        await File.WriteAllTextAsync(Path.Combine(roots.AssistantDataRoot, "install-state.json"), "state");
        await File.WriteAllTextAsync(Path.Combine(roots.AssistantDataRoot, "customer.txt"), "keep");
        await File.WriteAllTextAsync(Path.Combine(downloads, "ChatGPT-x64.msix"), "keep");
        var service = new SelectiveCleanupService(new EmptySecretStore(), new MemoryEnvironment(), roots);

        var result = await service.CleanAsync(
            [CodexArtifactIds.AssistantData],
            downloads,
            previousCodexCliPath: null,
            default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(roots.AssistantDataRoot, "install-state.json")));
        Assert.True(File.Exists(Path.Combine(roots.AssistantDataRoot, "customer.txt")));
        Assert.True(File.Exists(Path.Combine(downloads, "ChatGPT-x64.msix")));
    }

    [Fact]
    public async Task CleanAsync_DeletesSafeEffectiveCodexHomeWithoutDeletingDefaultHome()
    {
        var roots = CreateRoots();
        var customHome = Path.Combine(roots.UserProfile, "CustomCodexHome");
        Directory.CreateDirectory(customHome);
        Directory.CreateDirectory(roots.CodexHome);
        var service = new SelectiveCleanupService(new EmptySecretStore(), new MemoryEnvironment(), roots);

        var result = await service.CleanAsync(
            [CodexArtifactIds.CodexHome],
            Path.Combine(root, "downloads"),
            previousCodexCliPath: null,
            effectiveCodexHome: customHome,
            default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(Directory.Exists(customHome));
        Assert.True(Directory.Exists(roots.CodexHome));
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

    private sealed class EmptySecretStore : ISecretStore
    {
        public OperationResult<Unit> Write(string target, string secret) => OperationResult<Unit>.Success(default);
        public OperationResult<string> Read(string target) => OperationResult<string>.Failure("credential.not_found", "not found");
        public OperationResult<Unit> Delete(string target) => OperationResult<Unit>.Success(default);
    }

    private sealed class MemoryEnvironment : IUserEnvironment
    {
        public string? Value { get; set; }
        public string? GetCodexCliPath() => Value;
        public void SetCodexCliPath(string? value) => Value = value;
    }
}
