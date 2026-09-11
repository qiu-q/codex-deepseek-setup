using System.Globalization;
using System.Text;
using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Core.Security;

namespace CodexDeepSeekSetup.App.Logic.Diagnostics;

public sealed class DiagnosticLog
{
    private readonly object sync = new();
    private readonly Func<DateTimeOffset> clock;
    private readonly List<string> entries = [];

    public DiagnosticLog(string logDirectory, Func<DateTimeOffset>? clock = null, string? sessionDescription = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        this.clock = clock ?? (() => DateTimeOffset.Now);
        var startedAt = this.clock();
        Directory.CreateDirectory(logDirectory);
        FilePath = Path.Combine(
            logDirectory,
            $"CodexDeepSeekSetup-diagnostic-{startedAt:yyyyMMdd-HHmmss}.txt");
        AppendRaw("Codex + DeepSeek 安装助手诊断日志");
        AppendRaw($"session={SecretRedactor.Redact(sessionDescription ?? "unspecified")}");
        AppendRaw($"started={startedAt.ToString("O", CultureInfo.InvariantCulture)}");
        AppendRaw("privacy=API Key、Bearer 凭据和 URL 查询参数会自动隐藏");
    }

    public string FilePath { get; }

    public void Write(string category, string message)
    {
        var safeCategory = SecretRedactor.Redact(category).ReplaceLineEndings(" ");
        var safeMessage = SecretRedactor.Redact(message).ReplaceLineEndings(" | ");
        AppendRaw($"{clock().ToString("O", CultureInfo.InvariantCulture)} [{safeCategory}] {safeMessage}");
    }

    public void RecordResult<T>(string category, OperationResult<T>? result)
    {
        if (result is null)
        {
            Write(category, "result=cancelled");
            return;
        }

        if (result.IsSuccess)
        {
            Write(category, "result=success");
            return;
        }

        var details = string.IsNullOrWhiteSpace(result.DiagnosticDetails)
            ? string.Empty
            : $"; details={result.DiagnosticDetails}";
        Write(
            category,
            $"result=failure; code={result.ErrorCode ?? "unknown"}; message={result.ErrorMessage ?? "操作失败"}{details}");
    }

    public void RecordException(string category, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var details = new StringBuilder();
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (details.Length > 0)
            {
                details.Append(" -> ");
            }
            details.Append(current.GetType().Name).Append(": ").Append(current.Message);
        }
        Write(category, $"result=exception; {details}");
    }

    public string Snapshot()
    {
        lock (sync)
        {
            return string.Join(Environment.NewLine, entries) + Environment.NewLine;
        }
    }

    public OperationResult<string> ExportTo(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, Path.GetFileName(FilePath));
            File.WriteAllText(destination, Snapshot(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return OperationResult<string>.Success(destination);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return OperationResult<string>.Failure(
                "diagnostics.export.failed",
                "无法导出诊断日志",
                $"{error.GetType().Name}: {error.Message}");
        }
    }

    private void AppendRaw(string line)
    {
        lock (sync)
        {
            entries.Add(line);
            try
            {
                File.WriteAllText(FilePath, SnapshotUnsafe(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The in-memory log remains available for copying even if disk persistence is blocked.
            }
        }
    }

    private string SnapshotUnsafe() => string.Join(Environment.NewLine, entries) + Environment.NewLine;
}
