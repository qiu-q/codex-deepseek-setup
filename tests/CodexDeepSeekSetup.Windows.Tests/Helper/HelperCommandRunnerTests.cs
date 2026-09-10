using CodexDeepSeekSetup.Helper;

namespace CodexDeepSeekSetup.Windows.Tests.Helper;

public sealed class HelperCommandRunnerTests
{
    [Fact]
    public async Task CredentialRead_AllowsOnlyDeepSeekTargetAndWritesSecret()
    {
        var credentials = new FakeCredentialReader("sk-secret");
        var runner = new HelperCommandRunner(credentials, new FakeFinalizer());
        using var output = new StringWriter();

        var exitCode = await runner.ExecuteAsync(
            ["credential", "read", "--target", HelperCommandRunner.DeepSeekCredentialTarget],
            output,
            TextWriter.Null,
            default);

        Assert.Equal(0, exitCode);
        Assert.Equal("sk-secret", output.ToString());
        Assert.Equal(HelperCommandRunner.DeepSeekCredentialTarget, credentials.LastTarget);
    }

    [Fact]
    public async Task CredentialRead_RejectsArbitraryCredentialTarget()
    {
        var runner = new HelperCommandRunner(new FakeCredentialReader("secret"), new FakeFinalizer());

        var exitCode = await runner.ExecuteAsync(
            ["credential", "read", "--target", "Other/Secret"],
            TextWriter.Null,
            TextWriter.Null,
            default);

        Assert.Equal(64, exitCode);
    }

    [Fact]
    public async Task FinalizeCleanup_DelegatesValidatedArguments()
    {
        var finalizer = new FakeFinalizer();
        var runner = new HelperCommandRunner(new FakeCredentialReader("secret"), finalizer);

        var exitCode = await runner.ExecuteAsync(
            ["finalize-cleanup", "123", @"D:\Setup"],
            TextWriter.Null,
            TextWriter.Null,
            default);

        Assert.Equal(0, exitCode);
        Assert.Equal((123, @"D:\Setup"), finalizer.LastRequest);
    }

    [Fact]
    public async Task SelfDeleteFinalizer_RemovesPublishedFilesAndEmptyDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "codex-helper-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "CodexDeepSeekSetup.exe"), "main");
        await File.WriteAllTextAsync(Path.Combine(directory, "CodexDeepSeekSetup.Helper.exe"), "helper");
        var finalizer = new HelperSelfDeleteFinalizer(
            waitForProcess: (_, _) => Task.CompletedTask,
            scheduleSelfDelete: () => { });

        var result = await finalizer.RunAsync(123, directory, default);

        Assert.True(result);
        Assert.False(Directory.Exists(directory));
    }

    private sealed class FakeCredentialReader(string secret) : IHelperCredentialReader
    {
        public string? LastTarget { get; private set; }

        public string? Read(string target)
        {
            LastTarget = target;
            return secret;
        }
    }

    private sealed class FakeFinalizer : IHelperFinalizer
    {
        public (int ParentPid, string Directory)? LastRequest { get; private set; }

        public Task<bool> RunAsync(int parentPid, string directory, CancellationToken cancellationToken)
        {
            LastRequest = (parentPid, directory);
            return Task.FromResult(true);
        }
    }
}
