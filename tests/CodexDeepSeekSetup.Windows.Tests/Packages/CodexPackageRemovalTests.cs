using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Packages;
using CodexDeepSeekSetup.Windows.Processes;
using CodexDeepSeekSetup.Windows.Security;
using System.Runtime.Versioning;

namespace CodexDeepSeekSetup.Windows.Tests.Packages;

[SupportedOSPlatform("windows")]
public sealed class CodexPackageRemovalTests
{
    [Fact]
    public async Task RemoveCodexAsync_UsesOnlyFixedOpenAiPackageIdentity()
    {
        var runner = new RecordingProcessRunner();
        var operations = new DefaultRestrictedOperations(
            new EmptySecretStore(),
            new CodexPackageVerifier(new AlwaysValidSignatureVerifier()),
            runner);

        var exitCode = await operations.RemoveCodexAsync(TextWriter.Null, TextWriter.Null, default);

        Assert.Equal(0, exitCode);
        Assert.Equal("powershell.exe", runner.FileName);
        var script = Assert.Single(runner.Arguments.Where(argument => argument.Contains("Remove-AppxPackage", StringComparison.Ordinal)));
        Assert.Contains("OpenAI.Codex", script, StringComparison.Ordinal);
        Assert.Contains("Remove-AppxPackage", script, StringComparison.Ordinal);
        Assert.Contains("Remove-AppxProvisionedPackage", script, StringComparison.Ordinal);
        Assert.Null(runner.Environment);
    }

    [Fact]
    public async Task EnableBuiltInAdministratorCompatibilityAsync_UsesFixedPolicyRunOnceAndRestartScript()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"codex-admin-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "CodexDeepSeekSetup.exe");
        await File.WriteAllBytesAsync(executable, [0]);
        try
        {
            var runner = new RecordingProcessRunner();
            var operations = new DefaultRestrictedOperations(
                new EmptySecretStore(),
                new CodexPackageVerifier(new AlwaysValidSignatureVerifier()),
                runner,
                () => executable);

            var exitCode = await operations.EnableBuiltInAdministratorCompatibilityAsync(
                TextWriter.Null,
                TextWriter.Null,
                default);

            Assert.Equal(0, exitCode);
            Assert.Equal("powershell.exe", runner.FileName);
            var script = Assert.Single(runner.Arguments.Where(argument =>
                argument.Contains("FilterAdministratorToken", StringComparison.Ordinal)));
            Assert.Contains("RunOnce", script, StringComparison.Ordinal);
            Assert.Contains("shutdown.exe /r /t 15", script, StringComparison.Ordinal);
            Assert.Contains("EndsWith('-500')", script, StringComparison.Ordinal);
            Assert.Equal(Path.GetFullPath(executable), runner.Environment?["CODEX_SETUP_RELAUNCH"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public string? FileName { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public IReadOnlyDictionary<string, string?>? Environment { get; private set; }

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string?>? environment,
            CancellationToken cancellationToken)
        {
            FileName = fileName;
            Arguments = arguments;
            Environment = environment;
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class AlwaysValidSignatureVerifier : ISignatureVerifier
    {
        public Task<bool> IsValidAsync(string filePath, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class EmptySecretStore : ISecretStore
    {
        public OperationResult<Unit> Write(string target, string secret) => OperationResult<Unit>.Success(default);
        public OperationResult<string> Read(string target) => OperationResult<string>.Failure("credential.not_found", "not found");
        public OperationResult<Unit> Delete(string target) => OperationResult<Unit>.Success(default);
    }
}
