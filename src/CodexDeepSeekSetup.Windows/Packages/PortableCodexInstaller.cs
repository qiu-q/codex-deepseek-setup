using System.Globalization;
using System.IO.Compression;
using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Processes;

namespace CodexDeepSeekSetup.Windows.Packages;

public sealed record PortableCodexInstall(
    string InstallRoot,
    string AppPath,
    string CliPath,
    string Version);

public sealed class PortableCodexInstaller(
    CodexPackageVerifier verifier,
    IProcessRunner processRunner)
{
    public static PortableCodexInstall? TryRecover(string destinationRoot, string? persistedCliPath)
    {
        if (string.IsNullOrWhiteSpace(persistedCliPath))
        {
            return null;
        }

        try
        {
            var allowedRoot = Path.GetFullPath(destinationRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var cliPath = Path.GetFullPath(persistedCliPath);
            if (!cliPath.StartsWith(allowedRoot, PathComparison))
            {
                return null;
            }

            var resourcesDirectory = Path.GetDirectoryName(cliPath);
            var appDirectory = resourcesDirectory is null ? null : Path.GetDirectoryName(resourcesDirectory);
            var installRoot = appDirectory is null ? null : Path.GetDirectoryName(appDirectory);
            if (installRoot is null)
            {
                return null;
            }

            var expectedCli = Path.Combine(installRoot, "app", "resources", "codex.exe");
            var appPath = Path.Combine(installRoot, "app", "ChatGPT.exe");
            if (!string.Equals(cliPath, expectedCli, PathComparison) ||
                !File.Exists(cliPath) ||
                !File.Exists(appPath))
            {
                return null;
            }

            return new PortableCodexInstall(installRoot, appPath, cliPath, Path.GetFileName(installRoot));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public async Task<OperationResult<PortableCodexInstall>> InstallAsync(
        string msixPath,
        string licensePath,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        var verified = await verifier.VerifyAsync(msixPath, licensePath, cancellationToken).ConfigureAwait(false);
        if (!verified.IsSuccess)
        {
            return OperationResult<PortableCodexInstall>.Failure(
                verified.ErrorCode!,
                verified.ErrorMessage!);
        }

        try
        {
            var version = Version.Parse(verified.Value!.Version).ToString();
            var installRoot = Path.Combine(
                Path.GetFullPath(destinationRoot),
                $"{version}-{DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}");
            var rootPrefix = installRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            using var archive = ZipFile.OpenRead(msixPath);
            foreach (var entry in archive.Entries)
            {
                var entryPath = Path.GetFullPath(Path.Combine(installRoot, entry.FullName));
                if (!entryPath.StartsWith(rootPrefix, PathComparison) &&
                    !string.Equals(entryPath, installRoot, PathComparison))
                {
                    return OperationResult<PortableCodexInstall>.Failure(
                        "portable.archive.unsafe",
                        "安装包包含越过解包目录的路径，已停止实验性安装。");
                }
            }

            Directory.CreateDirectory(installRoot);
            archive.ExtractToDirectory(installRoot, overwriteFiles: true);

            var appPath = Path.Combine(installRoot, "app", "ChatGPT.exe");
            var cliPath = Path.Combine(installRoot, "app", "resources", "codex.exe");
            if (!File.Exists(appPath) || !File.Exists(cliPath))
            {
                return OperationResult<PortableCodexInstall>.Failure(
                    "portable.files.missing",
                    "官方包解压完成，但缺少 ChatGPT.exe 或 Codex CLI。");
            }

            var cliCheck = await processRunner.RunAsync(cliPath, ["--version"], null, cancellationToken)
                .ConfigureAwait(false);
            if (cliCheck.ExitCode != 0)
            {
                return OperationResult<PortableCodexInstall>.Failure(
                    "portable.cli.failed",
                    "实验性目录已生成，但其中的 Codex CLI 无法运行。");
            }

            return OperationResult<PortableCodexInstall>.Success(
                new PortableCodexInstall(installRoot, appPath, cliPath, version));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or FormatException)
        {
            return OperationResult<PortableCodexInstall>.Failure(
                "portable.extract.failed",
                "无法解压官方 MSIX。请检查磁盘空间、目录权限和 Windows 长路径设置。");
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
