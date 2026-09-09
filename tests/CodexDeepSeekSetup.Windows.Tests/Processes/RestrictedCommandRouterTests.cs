using CodexDeepSeekSetup.Windows.Processes;

namespace CodexDeepSeekSetup.Windows.Tests.Processes;

public sealed class RestrictedCommandRouterTests : IDisposable
{
    private readonly string requestRoot = Path.Combine(Path.GetTempPath(), "codex-helper-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("powershell", "-Command", "whoami")]
    [InlineData("elevated", "delete", "C:\\")]
    [InlineData("credential", "read", "--target", "AnotherSecret")]
    public async Task ExecuteAsync_RejectsUnknownOrUnauthorizedOperations(params string[] args)
    {
        var output = new StringWriter();
        var router = new RestrictedCommandRouter(new VisibleOperations(), requestRoot);

        var exitCode = await router.ExecuteAsync(args, output, output, default);

        Assert.Equal(64, exitCode);
        Assert.DoesNotContain("operation-ran", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_AllowsOnlyTheDeepSeekCredentialTarget()
    {
        var output = new StringWriter();
        var router = new RestrictedCommandRouter(new VisibleOperations(), requestRoot);

        var exitCode = await router.ExecuteAsync(
            ["credential", "read", "--target", CredentialTargets.DeepSeekApiKey],
            output,
            TextWriter.Null,
            default);

        Assert.Equal(0, exitCode);
        Assert.Equal("credential-value", output.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_AllowsOwnedJsonAppxRequest()
    {
        Directory.CreateDirectory(requestRoot);
        var request = Path.Combine(requestRoot, "install.json");
        await File.WriteAllTextAsync(request, "{}");
        var output = new StringWriter();
        var router = new RestrictedCommandRouter(new VisibleOperations(), requestRoot);

        var exitCode = await router.ExecuteAsync(
            ["elevated", "appx-install", request],
            output,
            TextWriter.Null,
            default);

        Assert.Equal(0, exitCode);
        Assert.Equal("appx-request-ok", output.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_AllowsOwnedJsonVolumeRequestOnly()
    {
        Directory.CreateDirectory(requestRoot);
        var request = Path.Combine(requestRoot, "volume.json");
        await File.WriteAllTextAsync(request, "{}");
        var output = new StringWriter();
        var router = new RestrictedCommandRouter(new VisibleOperations(), requestRoot);

        var accepted = await router.ExecuteAsync(
            ["elevated", "appx-prepare-volume", request],
            output,
            TextWriter.Null,
            default);
        var rejected = await router.ExecuteAsync(
            ["elevated", "appx-prepare-volume", @"C:\outside.json"],
            output,
            TextWriter.Null,
            default);

        Assert.Equal(0, accepted);
        Assert.Equal(RestrictedCommandRouter.UsageError, rejected);
        Assert.Contains("volume-request-ok", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RoutesOnlyExactAppxRemoveCommand()
    {
        var output = new StringWriter();
        var operations = new VisibleOperations();
        var router = new RestrictedCommandRouter(operations, requestRoot);

        var accepted = await router.ExecuteAsync(
            ["elevated", "appx-remove"],
            output,
            TextWriter.Null,
            default);
        var rejected = await router.ExecuteAsync(
            ["elevated", "appx-remove", "C:\\"],
            output,
            TextWriter.Null,
            default);

        Assert.Equal(0, accepted);
        Assert.Equal(RestrictedCommandRouter.UsageError, rejected);
        Assert.True(operations.RemoveCodexCalled);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAppxRequestOutsideOwnedDirectory()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(outside, "{}");
        try
        {
            var router = new RestrictedCommandRouter(new VisibleOperations(), requestRoot);

            var exitCode = await router.ExecuteAsync(
                ["elevated", "appx-install", outside],
                TextWriter.Null,
                TextWriter.Null,
                default);

            Assert.Equal(64, exitCode);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(requestRoot))
        {
            Directory.Delete(requestRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class VisibleOperations : IRestrictedOperations
    {
        public bool RemoveCodexCalled { get; private set; }

        public Task<int> ReadCredentialAsync(string target, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        {
            output.Write("credential-value");
            return Task.FromResult(0);
        }

        public Task<int> InstallAppxAsync(string requestFile, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        {
            output.Write("appx-request-ok");
            return Task.FromResult(0);
        }

        public Task<int> PrepareAppxVolumeAsync(string requestFile, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        {
            output.Write("volume-request-ok");
            return Task.FromResult(0);
        }

        public Task<int> RemoveCodexAsync(TextWriter output, TextWriter error, CancellationToken cancellationToken)
        {
            RemoveCodexCalled = true;
            output.Write("appx-remove-ok");
            return Task.FromResult(0);
        }

        public Task<int> StartServiceAsync(string serviceName, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        {
            output.Write("service-ok");
            return Task.FromResult(0);
        }

        public Task<int> ResumeAsync(TextWriter output, TextWriter error, CancellationToken cancellationToken)
        {
            output.Write("resume-ok");
            return Task.FromResult(0);
        }
    }
}
