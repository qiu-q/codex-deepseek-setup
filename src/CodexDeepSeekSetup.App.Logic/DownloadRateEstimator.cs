using System.Globalization;

namespace CodexDeepSeekSetup.App.Logic;

public sealed record DownloadMetrics(
    double? FilePercent,
    double? BytesPerSecond,
    TimeSpan? EstimatedRemaining,
    string SizeText,
    string SpeedText,
    string EtaText);

public sealed class DownloadRateEstimator
{
    private const double SmoothingFactor = 0.3;
    private readonly Dictionary<string, Sample> samples = new(StringComparer.OrdinalIgnoreCase);

    public DownloadMetrics Update(SetupProgress progress, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(progress.FileName) || progress.BytesCompleted is null)
        {
            return new DownloadMetrics(null, null, null, string.Empty, string.Empty, string.Empty);
        }

        var fileName = progress.FileName;
        var bytes = Math.Max(0, progress.BytesCompleted.Value);
        var total = progress.TotalBytes is > 0 ? progress.TotalBytes : null;
        double? rate = null;

        if (samples.TryGetValue(fileName, out var previous))
        {
            var seconds = (timestamp - previous.Timestamp).TotalSeconds;
            var delta = bytes - previous.Bytes;
            if (seconds > 0 && delta >= 0)
            {
                var instantaneous = delta / seconds;
                rate = previous.SmoothedBytesPerSecond is > 0
                    ? previous.SmoothedBytesPerSecond.Value * (1 - SmoothingFactor) + instantaneous * SmoothingFactor
                    : instantaneous;
            }
        }

        samples[fileName] = new Sample(bytes, timestamp, rate);
        double? percent = total is long totalBytes
            ? Math.Clamp(bytes * 100d / totalBytes, 0, 100)
            : null;
        TimeSpan? remaining = total is long knownTotal && rate is > 0
            ? TimeSpan.FromSeconds(Math.Max(0, knownTotal - bytes) / rate.Value)
            : null;

        return new DownloadMetrics(
            percent,
            rate,
            remaining,
            total is long size ? $"{FormatBytes(bytes)} / {FormatBytes(size)}" : $"{FormatBytes(bytes)} 已下载",
            rate is > 0 ? $"{FormatBytes(rate.Value)}/s" : "计算中…",
            total is null ? "总大小未知" : remaining is null ? "正在估算…" : FormatEta(remaining.Value));
    }

    private static string FormatBytes(double bytes)
    {
        const double kib = 1024;
        const double mib = kib * 1024;
        const double gib = mib * 1024;
        return bytes switch
        {
            >= gib => (bytes / gib).ToString("0.00", CultureInfo.InvariantCulture) + " GB",
            >= mib => (bytes / mib).ToString("0.00", CultureInfo.InvariantCulture) + " MB",
            >= kib => (bytes / kib).ToString("0.00", CultureInfo.InvariantCulture) + " KB",
            _ => bytes.ToString("0", CultureInfo.InvariantCulture) + " B"
        };
    }

    private static string FormatEta(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        if (seconds < 60)
        {
            return $"约 {seconds} 秒";
        }

        var minutes = (int)Math.Ceiling(seconds / 60d);
        return minutes < 60 ? $"约 {minutes} 分钟" : $"约 {(int)Math.Ceiling(minutes / 60d)} 小时";
    }

    private sealed record Sample(long Bytes, DateTimeOffset Timestamp, double? SmoothedBytesPerSecond);
}
