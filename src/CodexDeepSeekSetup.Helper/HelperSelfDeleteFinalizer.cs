using System.Diagnostics;

namespace CodexDeepSeekSetup.Helper;

public sealed class HelperSelfDeleteFinalizer(
    Func<int, CancellationToken, Task>? waitForProcess = null,
    Action? scheduleSelfDelete = null) : IHelperFinalizer
{
    private static readonly string[] PublishedFileNames =
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
    private readonly Func<int, CancellationToken, Task> wait = waitForProcess ?? WaitForProcessAsync;
    private readonly Action schedule = scheduleSelfDelete ?? ScheduleCurrentExecutableDeletion;

    public async Task<bool> RunAsync(int parentPid, string directory, CancellationToken cancellationToken)
    {
        var validated = Validate(directory);
        if (validated is null)
        {
            return false;
        }

        try
        {
            if (parentPid > 0)
            {
                await wait(parentPid, cancellationToken).ConfigureAwait(false);
            }

            foreach (var name in PublishedFileNames)
            {
                var path = Path.Combine(validated, name);
                for (var attempt = 0; attempt < 20 && File.Exists(path); attempt++)
                {
                    try
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                    }
                    catch (IOException) when (attempt < 19)
                    {
                        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    }
                    catch (UnauthorizedAccessException) when (attempt < 19)
                    {
                        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            if (Directory.Exists(validated) && !Directory.EnumerateFileSystemEntries(validated).Any())
            {
                Directory.Delete(validated);
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                schedule();
            }
            catch
            {
            }
        }
    }

    private static string? Validate(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(full);
        return string.IsNullOrWhiteSpace(root) ||
               string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
               !PublishedFileNames.Any(name => File.Exists(Path.Combine(full, name)))
            ? null
            : full;
    }

    private static void ScheduleCurrentExecutableDeletion()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            return;
        }

        const string script = "$path=$env:CODEX_CLEANUP_SELF;for($i=0;$i-lt40;$i++){Start-Sleep -Milliseconds 250;Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue;if(-not(Test-Path -LiteralPath $path)){break}}";
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.Environment["CODEX_CLEANUP_SELF"] = executable;
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-WindowStyle");
        info.ArgumentList.Add("Hidden");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(script);
        Process.Start(info);
    }

    private static async Task WaitForProcessAsync(int parentPid, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(parentPid);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
        }
    }
}
