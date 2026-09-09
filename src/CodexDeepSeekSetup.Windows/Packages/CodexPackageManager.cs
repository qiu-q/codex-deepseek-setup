using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Processes;
using CodexDeepSeekSetup.Windows.Storage;

namespace CodexDeepSeekSetup.Windows.Packages;

public sealed record CodexInstallRequest(string MsixPath, string LicensePath, string PackageVersion);
public sealed record CodexVolumeRequest(string DriveRoot);

public interface ICodexPackageManager
{
    Task<OperationResult<Unit>> InstallAsync(string msixPath, string licensePath, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> RegisterCurrentUserAsync(string msixPath, CancellationToken cancellationToken);
    Task<OperationResult<CodexPackageStatus>> GetStatusAsync(CancellationToken cancellationToken);
    Task<OperationResult<Unit>> MoveCurrentUserPackageAsync(string driveRoot, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> LaunchAndVerifyAsync(CancellationToken cancellationToken);
    Task<OperationResult<Unit>> RemoveAsync(CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class CodexPackageManager(
    CodexPackageVerifier verifier,
    IProcessRunner processRunner,
    string elevatedExecutablePath,
    string requestDirectory) : ICodexPackageManager
{
    private const string PackageFamily = "OpenAI.Codex_2p2nqsd0c76g0";
    private const string AppUserModelId = PackageFamily + "!App";
    private string? expectedVersion;

    public async Task<OperationResult<Unit>> InstallAsync(
        string msixPath,
        string licensePath,
        CancellationToken cancellationToken)
    {
        var verified = await verifier.VerifyAsync(msixPath, licensePath, cancellationToken).ConfigureAwait(false);
        if (!verified.IsSuccess)
        {
            return OperationResult<Unit>.Failure(verified.ErrorCode!, verified.ErrorMessage!);
        }
        expectedVersion = verified.Value!.Version;

        const string installedCheck = "$p=Get-AppxPackage -Name OpenAI.Codex -ErrorAction SilentlyContinue | Where-Object { [version]$_.Version -ge [version]$env:CODEX_SETUP_VERSION -and $_.Status -eq 'Ok' }; if($p){exit 0}else{exit 1}";
        var alreadyInstalled = await processRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", installedCheck],
            new Dictionary<string, string?> { ["CODEX_SETUP_VERSION"] = expectedVersion },
            cancellationToken).ConfigureAwait(false);
        if (alreadyInstalled.ExitCode == 0)
        {
            return OperationResult<Unit>.Success(default);
        }

        if (!File.Exists(elevatedExecutablePath))
        {
            return OperationResult<Unit>.Failure("install.elevation.missing", "无法定位当前安装程序，不能启动受控提权模式。");
        }

        Directory.CreateDirectory(requestDirectory);
        var requestPath = Path.Combine(requestDirectory, $"install-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(new CodexInstallRequest(
                Path.GetFullPath(msixPath),
                Path.GetFullPath(licensePath),
                verified.Value.Version)),
            cancellationToken).ConfigureAwait(false);
        try
        {
            var startInfo = new ProcessStartInfo(elevatedExecutablePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(elevatedExecutablePath)!
            };
            startInfo.ArgumentList.Add("elevated");
            startInfo.ArgumentList.Add("appx-install");
            startInfo.ArgumentList.Add(requestPath);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return OperationResult<Unit>.Failure("install.elevation.failed", "无法启动管理员安装助手。");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0
                ? OperationResult<Unit>.Success(default)
                : OperationResult<Unit>.Failure("install.appx.failed", $"Codex 离线部署失败（退出码 {process.ExitCode}）。");
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return OperationResult<Unit>.Failure("install.elevation.cancelled", "已取消管理员授权，Codex 尚未安装。");
        }
        finally
        {
            File.Delete(requestPath);
        }
    }

    public async Task<OperationResult<Unit>> RegisterCurrentUserAsync(string msixPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            return OperationResult<Unit>.Failure("install.version.missing", "安装包版本尚未验证。");
        }

        const string script = """
            $ErrorActionPreference='Stop'
            $existing=Get-AppxPackage -Name OpenAI.Codex -ErrorAction SilentlyContinue | Where-Object { [version]$_.Version -ge [version]$env:CODEX_SETUP_VERSION -and $_.Status -eq 'Ok' }
            if(-not $existing){Add-AppxPackage -Path $env:CODEX_SETUP_MSIX -ForceApplicationShutdown}
            if(-not (Get-AppxPackage -Name OpenAI.Codex -ErrorAction SilentlyContinue | Where-Object { [version]$_.Version -ge [version]$env:CODEX_SETUP_VERSION -and $_.Status -eq 'Ok' })){exit 2}
            """;
        var result = await processRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            new Dictionary<string, string?>
            {
                ["CODEX_SETUP_MSIX"] = Path.GetFullPath(msixPath),
                ["CODEX_SETUP_VERSION"] = expectedVersion
            },
            cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? OperationResult<Unit>.Success(default)
            : OperationResult<Unit>.Failure("install.registration.failed", "系统已部署安装包，但当前用户注册失败。请注销 Windows 后重新登录再试。");
    }

    public async Task<OperationResult<Unit>> LaunchAndVerifyAsync(CancellationToken cancellationToken)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{AppUserModelId}") { UseShellExecute = true });
            for (var attempt = 0; attempt < 12; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                var hasWindow = Process.GetProcessesByName("ChatGPT")
                    .Concat(Process.GetProcessesByName("Codex"))
                    .Any(process =>
                    {
                        using (process)
                        {
                            return process.MainWindowHandle != IntPtr.Zero;
                        }
                    });
                if (hasWindow)
                {
                    return OperationResult<Unit>.Success(default);
                }
            }

            return OperationResult<Unit>.Failure(
                "launch.window.missing",
                "Codex 进程可能已启动，但没有检测到窗口。若当前账户是内置 Administrator，请启用管理员批准模式或改用普通管理员账户。");
        }
        catch
        {
            return OperationResult<Unit>.Failure("launch.failed", "无法启动 Codex，请从开始菜单手动打开 ChatGPT/Codex。");
        }
    }

    public async Task<OperationResult<CodexPackageStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        const string script = "$p=Get-AppxPackage -Name OpenAI.Codex -ErrorAction SilentlyContinue | Where-Object Status -eq 'Ok' | Select-Object -First 1 Name,Version,PackageFullName,InstallLocation,Status; if($p){$p | ConvertTo-Json -Compress}";
        try
        {
            var result = await processRunner.RunAsync(
                "powershell.exe",
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
                null,
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return OperationResult<CodexPackageStatus>.Failure("package.status.failed", "无法读取 Codex 安装状态。");
            }

            return OperationResult<CodexPackageStatus>.Success(CodexPackageStatusParser.Parse(result.StandardOutput));
        }
        catch (JsonException)
        {
            return OperationResult<CodexPackageStatus>.Failure("package.status.invalid", "Windows 返回了无法识别的 Codex 安装状态。");
        }
    }

    public async Task<OperationResult<Unit>> MoveCurrentUserPackageAsync(
        string driveRoot,
        CancellationToken cancellationToken)
    {
        if (!StorageSelectionService.IsDriveRoot(driveRoot))
        {
            return OperationResult<Unit>.Failure("package.move.invalid_drive", "安装位置必须是磁盘根路径。");
        }

        var current = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess)
        {
            return OperationResult<Unit>.Failure(current.ErrorCode!, current.ErrorMessage!);
        }
        if (!current.Value!.IsInstalled)
        {
            return OperationResult<Unit>.Failure("package.move.not_installed", "没有检测到当前用户的 Codex 安装。");
        }
        if (string.Equals(current.Value.DriveRoot, driveRoot, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<Unit>.Success(default);
        }
        if (!File.Exists(elevatedExecutablePath))
        {
            return OperationResult<Unit>.Failure("package.move.elevation.missing", "无法定位当前安装助手，不能准备目标磁盘。");
        }

        Directory.CreateDirectory(requestDirectory);
        var requestPath = Path.Combine(requestDirectory, $"volume-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(new CodexVolumeRequest(Path.GetFullPath(driveRoot))),
            cancellationToken).ConfigureAwait(false);
        try
        {
            var startInfo = new ProcessStartInfo(elevatedExecutablePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(elevatedExecutablePath)!
            };
            startInfo.ArgumentList.Add("elevated");
            startInfo.ArgumentList.Add("appx-prepare-volume");
            startInfo.ArgumentList.Add(requestPath);
            using var helper = Process.Start(startInfo);
            if (helper is null)
            {
                return OperationResult<Unit>.Failure("package.move.elevation.failed", "无法启动目标磁盘准备程序。");
            }
            await helper.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (helper.ExitCode != 0)
            {
                return OperationResult<Unit>.Failure("package.move.volume.failed", $"无法准备目标 Windows 应用磁盘（退出码 {helper.ExitCode}）。");
            }
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return OperationResult<Unit>.Failure("package.move.elevation.cancelled", "已取消管理员授权，Codex 仍保留在原磁盘。");
        }
        finally
        {
            File.Delete(requestPath);
        }

        const string moveScript = """
            $ErrorActionPreference='Stop'
            Get-Process -Name ChatGPT,Codex -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            $package=Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop | Select-Object -First 1
            $volume=Get-AppxVolume -Path $env:CODEX_SETUP_DRIVE -ErrorAction Stop
            Move-AppxPackage -Package $package.PackageFullName -Volume $volume -Confirm:$false
            $moved=Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop | Select-Object -First 1
            if(-not $moved.InstallLocation.StartsWith($env:CODEX_SETUP_DRIVE,[StringComparison]::OrdinalIgnoreCase)){exit 2}
            """;
        var moved = await processRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", moveScript],
            new Dictionary<string, string?> { ["CODEX_SETUP_DRIVE"] = driveRoot },
            cancellationToken).ConfigureAwait(false);
        return moved.ExitCode == 0
            ? OperationResult<Unit>.Success(default)
            : OperationResult<Unit>.Failure("package.move.failed", "Codex 已安装，但 Windows 未能把它迁移到目标磁盘。可以稍后在检测与清理页面重试。");
    }

    public async Task<OperationResult<Unit>> RemoveAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(elevatedExecutablePath))
        {
            return OperationResult<Unit>.Failure("cleanup.elevation.missing", "无法定位安装助手，不能启动管理员清理。");
        }

        try
        {
            var startInfo = new ProcessStartInfo(elevatedExecutablePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(elevatedExecutablePath)!
            };
            startInfo.ArgumentList.Add("elevated");
            startInfo.ArgumentList.Add("appx-remove");
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return OperationResult<Unit>.Failure("cleanup.elevation.failed", "无法启动管理员清理进程。");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0
                ? OperationResult<Unit>.Success(default)
                : OperationResult<Unit>.Failure("cleanup.appx.failed", $"Codex 系统包清理失败（退出码 {process.ExitCode}）。");
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return OperationResult<Unit>.Failure("cleanup.elevation.cancelled", "已取消 Windows 管理员授权，尚未删除任何用户数据。");
        }
    }
}
