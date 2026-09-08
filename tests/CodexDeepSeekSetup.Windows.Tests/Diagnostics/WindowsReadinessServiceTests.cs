using CodexDeepSeekSetup.Windows.Diagnostics;
using CodexDeepSeekSetup.Windows.Processes;
using System.Runtime.Versioning;

namespace CodexDeepSeekSetup.Windows.Tests.Diagnostics;

[SupportedOSPlatform("windows")]
public sealed class WindowsReadinessServiceTests
{
    [Theory]
    [InlineData("19041", true, true)]
    [InlineData("19043", true, true)]
    [InlineData("19040", true, false)]
    [InlineData("19045", false, false)]
    public async Task CheckAsync_AllowsCompatibilityAttemptFromBuild19041OnX64(
        string build,
        bool isX64,
        bool expected)
    {
        var json = $$"""
            {"ProductName":"Windows 10 Pro","Version":"10.0.{{build}}","Build":"{{build}}","IsX64":{{isX64.ToString().ToLowerInvariant()}},"UserSid":"S-1-5-21-1-2-3-1001","EnableLua":1,"FilterAdministratorToken":0,"FreeBytes":10737418240,"Services":[],"IsCodexInstalled":false}
            """;
        var service = new WindowsReadinessService(new FixedProcessRunner(json));

        var result = await service.CheckAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(expected, result.Value!.IsSupported);
    }

    private sealed class FixedProcessRunner(string output) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string?>? environment,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, output, string.Empty));
    }
}
