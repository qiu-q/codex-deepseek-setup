using CodexDeepSeekSetup.App.Logic;

namespace CodexDeepSeekSetup.App.Tests.ViewModels;

public sealed class DownloadRateEstimatorTests
{
    [Fact]
    public void Update_UsesByteAndTimeDeltaForSpeedPercentageAndEta()
    {
        var estimator = new DownloadRateEstimator();
        var started = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        estimator.Update(Progress("ChatGPT-x64.msix", 1 * MiB, 5 * MiB), started);

        var result = estimator.Update(
            Progress("ChatGPT-x64.msix", 3 * MiB, 5 * MiB),
            started.AddSeconds(1));

        Assert.Equal(60, result.FilePercent);
        Assert.Equal(2 * MiB, result.BytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(1), result.EstimatedRemaining);
        Assert.Equal("3.00 MB / 5.00 MB", result.SizeText);
        Assert.Equal("2.00 MB/s", result.SpeedText);
        Assert.Equal("约 1 秒", result.EtaText);
    }

    [Fact]
    public void Update_WhenFileChanges_DoesNotReusePreviousFilesRate()
    {
        var estimator = new DownloadRateEstimator();
        var started = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        estimator.Update(Progress("ChatGPT-x64.msix", 2 * MiB, 5 * MiB), started);

        var result = estimator.Update(
            Progress("ChatGPT-License.xml", 1024, 2048),
            started.AddSeconds(1));

        Assert.Null(result.BytesPerSecond);
        Assert.Null(result.EstimatedRemaining);
        Assert.Equal("1.00 KB / 2.00 KB", result.SizeText);
    }

    [Fact]
    public void Update_WhenTotalIsUnknown_ShowsTransferredBytesWithoutFakeEta()
    {
        var estimator = new DownloadRateEstimator();

        var result = estimator.Update(
            Progress("ChatGPT-x64.msix", 2 * MiB, null),
            DateTimeOffset.Parse("2026-09-08T12:00:00Z"));

        Assert.Null(result.FilePercent);
        Assert.Null(result.EstimatedRemaining);
        Assert.Equal("2.00 MB 已下载", result.SizeText);
        Assert.Equal("计算中…", result.SpeedText);
        Assert.Equal("总大小未知", result.EtaText);
    }

    private const long MiB = 1024 * 1024;

    private static SetupProgress Progress(string fileName, long bytes, long? total) => new(
        "正在下载",
        Phase: SetupPhase.Download,
        FileName: fileName,
        BytesCompleted: bytes,
        TotalBytes: total);
}
