using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Cleanup;
using CodexDeepSeekSetup.Windows.Processes;
using CodexDeepSeekSetup.Windows.Security;

namespace CodexDeepSeekSetup.Windows.Tests.Cleanup;

public sealed class UserCleanupServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"codex-cleanup-{Guid.NewGuid():N}");

    [Fact]
    public async Task CleanAsync_DeletesEntireCodexHomeAndManagedRoots()
    {
        var roots = CreateRoots();
        foreach (var directory in roots.ManagedRoots)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "content.txt"), "delete");
        }
        var secrets = new RecordingSecretStore();
        var environment = new FakeUserEnvironment(roots.CliRoot + Path.DirectorySeparatorChar + "codex.exe");
        var service = new UserCleanupService(secrets, environment, roots);

        var result = await service.CleanAsync(previousCodexCliPath: null, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.All(roots.ManagedRoots, directory => Assert.False(Directory.Exists(directory)));
        Assert.True(secrets.DeepSeekCredentialDeleted);
        Assert.Null(environment.CodexCliPath);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("local")]
    [InlineData("desktop")]
    public void Validate_RejectsBroadDeletionRoot(string broadRoot)
    {
        var roots = CreateRoots();
        var unsafePath = broadRoot switch
        {
            "profile" => roots.UserProfile,
            "local" => roots.LocalAppData,
            _ => roots.Desktop
        };
        roots = roots with { CodexHome = unsafePath };

        Assert.Throws<InvalidOperationException>(roots.Validate);
    }

    [Fact]
    public async Task CleanAsync_RestoresRecordedEnvironmentValue()
    {
        var roots = CreateRoots();
        var environment = new FakeUserEnvironment(roots.CliRoot + Path.DirectorySeparatorChar + "codex.exe");
        var service = new UserCleanupService(new RecordingSecretStore(), environment, roots);

        var result = await service.CleanAsync(@"D:\preexisting\codex.exe", default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(@"D:\preexisting\codex.exe", environment.CodexCliPath);
    }

    [Fact]
    public async Task CleanAsync_WithoutLedger_PreservesUnrelatedEnvironmentValue()
    {
        var roots = CreateRoots();
        var environment = new FakeUserEnvironment(@"D:\unrelated\codex.exe");
        var service = new UserCleanupService(new RecordingSecretStore(), environment, roots);

        var result = await service.CleanAsync(previousCodexCliPath: null, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(@"D:\unrelated\codex.exe", environment.CodexCliPath);
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
        return new CleanupRoots(
            profile,
            local,
            Path.Combine(profile, "Desktop"),
            Path.Combine(profile, ".codex"),
            Path.Combine(local, "CodexDeepSeekSetup"),
            Path.Combine(local, "Programs", "CodexDeepSeekSetup"),
            Path.Combine(local, "Programs", "OpenAI", "Codex"),
            Path.Combine(local, "Programs", "OpenAI", "CodexPortable"));
    }

    private sealed class FakeUserEnvironment(string? codexCliPath) : IUserEnvironment
    {
        public string? CodexCliPath { get; private set; } = codexCliPath;
        public string? GetCodexCliPath() => CodexCliPath;
        public void SetCodexCliPath(string? value) => CodexCliPath = value;
    }

    private sealed class RecordingSecretStore : ISecretStore
    {
        public bool DeepSeekCredentialDeleted { get; private set; }
        public OperationResult<Unit> Write(string target, string secret) => OperationResult<Unit>.Success(default);
        public OperationResult<string> Read(string target) => OperationResult<string>.Failure("credential.not_found", "not found");
        public OperationResult<Unit> Delete(string target)
        {
            DeepSeekCredentialDeleted = target == CredentialTargets.DeepSeekApiKey;
            return OperationResult<Unit>.Success(default);
        }
    }
}
