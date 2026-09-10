using System.Diagnostics;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Windows.Cleanup;

public sealed class SelfDeleteFinalizer(
    string currentExecutablePath,
    Func<int, CancellationToken, Task>? waitForProcess = null,
    Action<string>? scheduleSelfDelete = null)
{
    public static readonly IReadOnlyList<string> PublishedFileNames =
    [
        "CodexDeepSeekSetup.exe",
        "CodexDeepSeekSetup.Helper.exe",
        "CodexDeepSeekSetup.NetworkHelper.exe",
        "wpfgfx_cor3.dll",
        "PresentationNative_cor3.dll",
        "vcruntime140_cor3.dll",
        "D3DCompiler_47_cor3.dll",
        "PenImc_cor3.dll"
    ];

    public static readonly IReadOnlyList<string> BundledPayloadFileNames =
    [
        "ChatGPT-x64.msix",
        "ChatGPT-License.xml"
    ];

    private readonly Func<int, CancellationToken, Task> wait = waitForProcess ?? WaitForProcessAsync;
    private readonly Action<string> schedule = scheduleSelfDelete ?? ScheduleSelfDelete;

    public async Task<OperationResult<Unit>> RunAsync(
        int parentPid,
        string appDirectory,
        CancellationToken cancellationToken)
    {
        string directory;
        try
        {
            directory = ValidateApplicationDirectory(appDirectory);
        }
        catch (InvalidOperationException exception)
        {
            return OperationResult<Unit>.Failure("cleanup.self.path.rejected", exception.Message);
        }

        try
        {
            if (parentPid > 0)
            {
                await wait(parentPid, cancellationToken).ConfigureAwait(false);
            }

            foreach (var name in PublishedFileNames)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }

            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }

            return OperationResult<Unit>.Success(default);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return OperationResult<Unit>.Failure(
                "cleanup.self.failed",
                "安装助手文件未能完全删除，请关闭程序后手动删除其所在目录。");
        }
        finally
        {
            try
            {
                schedule(currentExecutablePath);
            }
            catch
            {
            }
        }
    }

    private static string ValidateApplicationDirectory(string appDirectory)
    {
        if (string.IsNullOrWhiteSpace(appDirectory))
        {
            throw new InvalidOperationException("安装助手目录不能为空。");
        }

        var fullPath = Path.GetFullPath(appDirectory);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) ||
            string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("拒绝清理磁盘根目录。");
        }

        var full = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!PublishedFileNames.Any(name => File.Exists(Path.Combine(full, name))))
        {
            throw new InvalidOperationException("目标目录中没有安装助手发布文件。");
        }

        return full;
    }

    private static async Task WaitForProcessAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
        }
    }

    private static void ScheduleSelfDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        const string script = """
            $path=$env:CODEX_CLEANUP_SELF
            for($attempt=0;$attempt -lt 40;$attempt++){
                Start-Sleep -Milliseconds 250
                Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
                if(-not (Test-Path -LiteralPath $path)){break}
            }
            """;
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.Environment["CODEX_CLEANUP_SELF"] = Path.GetFullPath(path);
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);
        Process.Start(startInfo);
    }
}
