using System.Runtime.Versioning;
using System.Text.Json;
using CodexDeepSeekSetup.Windows.Packages;
using CodexDeepSeekSetup.Windows.Security;
using CodexDeepSeekSetup.Windows.Storage;

namespace CodexDeepSeekSetup.Windows.Processes;

[SupportedOSPlatform("windows")]
public sealed class DefaultRestrictedOperations : IRestrictedOperations
{
    private readonly ISecretStore secretStore;
    private readonly CodexPackageVerifier packageVerifier;
    private readonly IProcessRunner processRunner;
    private readonly Func<string?> processPathProvider;

    public DefaultRestrictedOperations(
        ISecretStore secretStore,
        CodexPackageVerifier packageVerifier,
        IProcessRunner processRunner,
        Func<string?>? processPathProvider = null)
    {
        this.secretStore = secretStore;
        this.packageVerifier = packageVerifier;
        this.processRunner = processRunner;
        this.processPathProvider = processPathProvider ?? (() => Environment.ProcessPath);
    }

    private sealed record AppxVolumeRequest(string DriveRoot);

    public Task<int> ReadCredentialAsync(
        string target,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = secretStore.Read(target);
        if (!result.IsSuccess)
        {
            error.Write(result.ErrorMessage);
            return Task.FromResult(1);
        }

        output.Write(result.Value);
        return Task.FromResult(0);
    }

    public async Task<int> InstallAppxAsync(
        string requestFile,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        CodexInstallRequest? request;
        try
        {
            await using var stream = File.OpenRead(requestFile);
            request = await JsonSerializer.DeserializeAsync<CodexInstallRequest>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            error.Write("安装请求无效。");
            return 65;
        }

        if (request is null)
        {
            return 65;
        }

        var verified = await packageVerifier.VerifyAsync(request.MsixPath, request.LicensePath, cancellationToken)
            .ConfigureAwait(false);
        if (!verified.IsSuccess)
        {
            error.Write(verified.ErrorMessage);
            return 66;
        }

        const string script = """
            $ErrorActionPreference='Stop'
            try {
                Add-AppxProvisionedPackage -Online -PackagePath $env:CODEX_SETUP_MSIX -LicensePath $env:CODEX_SETUP_LICENSE | Out-Null
            }
            catch {
                $existing=Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq 'OpenAI.Codex' -and [version]$_.Version -ge [version]$env:CODEX_SETUP_VERSION }
                if(-not $existing){throw}
            }
            if(-not (Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq 'OpenAI.Codex' -and [version]$_.Version -ge [version]$env:CODEX_SETUP_VERSION })){exit 2}
            """;
        var process = await processRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            new Dictionary<string, string?>
            {
                ["CODEX_SETUP_MSIX"] = Path.GetFullPath(request.MsixPath),
                ["CODEX_SETUP_LICENSE"] = Path.GetFullPath(request.LicensePath),
                ["CODEX_SETUP_VERSION"] = request.PackageVersion
            },
            cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            error.Write("AppX 部署失败。请在主程序中查看诊断说明。");
            return process.ExitCode == 0 ? 1 : process.ExitCode;
        }

        output.Write("Codex 已部署。");
        return 0;
    }

    public async Task<int> StartServiceAsync(
        string serviceName,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        const string script = "Start-Service -Name $env:CODEX_SETUP_SERVICE -ErrorAction Stop";
        var result = await processRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            new Dictionary<string, string?> { ["CODEX_SETUP_SERVICE"] = serviceName },
            cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    public async Task<int> EnableBuiltInAdministratorCompatibilityAsync(
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var relaunchPath = processPathProvider();
        if (string.IsNullOrWhiteSpace(relaunchPath) ||
            !File.Exists(relaunchPath) ||
            !string.Equals(Path.GetFileName(relaunchPath), "CodexDeepSeekSetup.exe", StringComparison.OrdinalIgnoreCase))
        {
            error.Write("无法确定安装助手的自动重开路径。");
            return 65;
        }

        const string script = """
            $ErrorActionPreference='Stop'
            $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            if(-not $sid.EndsWith('-500')){exit 65}
            $policy='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
            $enableLua=(Get-ItemProperty -Path $policy -Name 'EnableLUA' -ErrorAction Stop).EnableLUA
            if($enableLua -ne 1){exit 66}
            New-ItemProperty -Path $policy -Name 'FilterAdministratorToken' -PropertyType DWord -Value 1 -Force | Out-Null
            $runOnce='HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce'
            New-Item -Path $runOnce -Force | Out-Null
            $quoted='"' + $env:CODEX_SETUP_RELAUNCH + '"'
            New-ItemProperty -Path $runOnce -Name 'CodexDeepSeekSetupResume' -PropertyType String -Value $quoted -Force | Out-Null
            shutdown.exe /r /t 15 /d p:4:1 /c "Codex 安装助手正在启用内置 Administrator 兼容模式"
            if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
            """;
        var result = await processRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            new Dictionary<string, string?>
            {
                ["CODEX_SETUP_RELAUNCH"] = Path.GetFullPath(relaunchPath)
            },
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            error.Write(result.ExitCode switch
            {
                65 => "只有内置 Administrator（SID 末尾 -500）需要执行此操作。",
                66 => "Windows UAC 已关闭，请先恢复 EnableLUA=1 并重启。",
                _ => "无法启用内置 Administrator 兼容模式或安排重启。"
            });
            return result.ExitCode;
        }

        output.Write("兼容模式已启用，已安排重启。");
        return 0;
    }

    public async Task<int> PrepareAppxVolumeAsync(
        string requestFile,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        AppxVolumeRequest? request;
        try
        {
            await using var stream = File.OpenRead(requestFile);
            request = await JsonSerializer.DeserializeAsync<AppxVolumeRequest>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            error.Write("安装磁盘请求无效。");
            return 65;
        }

        if (request is null || !StorageSelectionService.IsDriveRoot(request.DriveRoot))
        {
            error.Write("安装磁盘必须是本地磁盘根路径。");
            return 65;
        }

        try
        {
            var drive = new DriveInfo(request.DriveRoot);
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed ||
                !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase) ||
                drive.AvailableFreeSpace < StorageSelectionService.MinimumInstallFreeBytes)
            {
                error.Write("目标磁盘必须是已就绪的本地固定 NTFS 磁盘，并且至少有 3 GB 可用空间。");
                return 65;
            }
        }
        catch
        {
            error.Write("无法读取目标磁盘。");
            return 65;
        }

        const string script = """
            $ErrorActionPreference='Stop'
            $drive=$env:CODEX_SETUP_DRIVE
            $volume=Get-AppxVolume -Path $drive -ErrorAction SilentlyContinue
            if(-not $volume){
                $volume=Add-AppxVolume -Path (Join-Path $drive 'WindowsApps')
            }
            if(-not (Get-AppxVolume -Path $drive -ErrorAction SilentlyContinue)){exit 2}
            """;
        var result = await processRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            new Dictionary<string, string?> { ["CODEX_SETUP_DRIVE"] = request.DriveRoot },
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            error.Write("无法准备目标 Windows 应用磁盘。");
            return result.ExitCode;
        }

        output.Write("Windows 应用磁盘已准备。 ");
        return 0;
    }

    public async Task<int> RemoveCodexAsync(
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        const string script = """
            $ErrorActionPreference='Stop'
            Get-Process -Name ChatGPT,Codex -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            Get-AppxPackage -AllUsers -Name OpenAI.Codex -ErrorAction SilentlyContinue | ForEach-Object {
                Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop
            }
            Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq 'OpenAI.Codex' | ForEach-Object {
                Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction Stop | Out-Null
            }
            if(Get-AppxPackage -AllUsers -Name OpenAI.Codex -ErrorAction SilentlyContinue){exit 2}
            if(Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq 'OpenAI.Codex'){exit 3}
            """;
        var result = await processRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            null,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            error.Write("Codex 系统包未能完全移除。");
            return result.ExitCode;
        }

        output.Write("Codex 系统包已移除。");
        return 0;
    }

    public Task<int> ResumeAsync(TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
