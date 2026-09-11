using CodexDeepSeekSetup.App.Logic.Diagnostics;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.App.Tests.Diagnostics;

public sealed class DiagnosticLogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codex-diagnostic-tests", Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset timestamp = new(2026, 9, 11, 17, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Write_RedactsCredentialsInMemoryAndOnDisk()
    {
        var log = new DiagnosticLog(root, () => timestamp, "test session");

        log.Write("network", "GET https://provider.example/sub?token=secret-value key=sk-abc123456789");

        var snapshot = log.Snapshot();
        var persisted = File.ReadAllText(log.FilePath);
        Assert.Contains("https://provider.example/sub?[REDACTED]", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abc123456789", snapshot, StringComparison.Ordinal);
        Assert.Equal(snapshot, persisted);
    }

    [Fact]
    public void RecordResult_IncludesMachineReadableFailureEvidence()
    {
        var log = new DiagnosticLog(root, () => timestamp, "test session");
        var result = OperationResult<Unit>.Failure(
            "proxy.runtime.download",
            "Mihomo 下载或解压失败",
            "HttpRequestException: HTTP 502 from https://mirror.example/file?token=secret-value");

        log.RecordResult("network-helper", result);

        var snapshot = log.Snapshot();
        Assert.Contains("result=failure", snapshot, StringComparison.Ordinal);
        Assert.Contains("code=proxy.runtime.download", snapshot, StringComparison.Ordinal);
        Assert.Contains("HTTP 502", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportTo_CreatesAReadableCopy()
    {
        var exportDirectory = Path.Combine(root, "Desktop");
        var log = new DiagnosticLog(root, () => timestamp, "test session");
        log.Write("startup", "ready");

        var result = log.ExportTo(exportDirectory);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(result.Value));
        Assert.Equal(log.Snapshot(), File.ReadAllText(result.Value!));
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
