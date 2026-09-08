using System.Runtime.Versioning;
using System.Text.Json;
using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Processes;

namespace CodexDeepSeekSetup.Windows.Diagnostics;

public sealed record ServiceReadiness(string Name, string Status, string StartType);

public sealed record WindowsReadinessReport(
    string ProductName,
    string Version,
    string Build,
    bool IsX64,
    string UserSid,
    bool IsBuiltInAdministrator,
    int EnableLua,
    int FilterAdministratorToken,
    long FreeSystemDriveBytes,
    IReadOnlyList<ServiceReadiness> Services,
    bool IsCodexInstalled,
    bool IsSupported);

public interface IWindowsReadinessService
{
    Task<OperationResult<WindowsReadinessReport>> CheckAsync(CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsReadinessService(IProcessRunner processRunner) : IWindowsReadinessService
{
    private const string ProbeScript = """
        $os=Get-CimInstance Win32_OperatingSystem
        $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $policy=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction SilentlyContinue
        $uac=$policy.EnableLUA
        $filterAdmin=$policy.FilterAdministratorToken
        $drive=Get-CimInstance Win32_LogicalDisk -Filter (\"DeviceID='\"+$env:SystemDrive+\"'\")
        $services=Get-CimInstance Win32_Service | Where-Object Name -in @('AppXSvc','ClipSVC','LicenseManager','StateRepository') | Select-Object Name,State,StartMode
        $codex=Get-AppxPackage -Name OpenAI.Codex -ErrorAction SilentlyContinue | Where-Object Status -eq 'Ok'
        [pscustomobject]@{ProductName=$os.Caption;Version=$os.Version;Build=$os.BuildNumber;IsX64=([Environment]::Is64BitOperatingSystem);UserSid=$sid;EnableLua=[int]$uac;FilterAdministratorToken=[int]$filterAdmin;FreeBytes=[long]$drive.FreeSpace;Services=@($services);IsCodexInstalled=[bool]$codex} | ConvertTo-Json -Depth 4 -Compress
        """;

    public async Task<OperationResult<WindowsReadinessReport>> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await processRunner.RunAsync(
                "powershell.exe",
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", ProbeScript],
                null,
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return OperationResult<WindowsReadinessReport>.Failure("readiness.probe.failed", "无法读取 Windows 安装环境。");
            }

            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            var services = root.GetProperty("Services").EnumerateArray()
                .Select(service => new ServiceReadiness(
                    service.GetProperty("Name").GetString() ?? string.Empty,
                    service.GetProperty("State").GetString() ?? string.Empty,
                    service.GetProperty("StartMode").GetString() ?? string.Empty))
                .ToArray();
            var version = root.GetProperty("Version").GetString() ?? string.Empty;
            var build = root.GetProperty("Build").GetString() ?? "0";
            var supported = root.GetProperty("IsX64").GetBoolean() &&
                int.TryParse(build, out var buildNumber) && buildNumber >= 19045;
            var sid = root.GetProperty("UserSid").GetString() ?? string.Empty;
            var report = new WindowsReadinessReport(
                root.GetProperty("ProductName").GetString() ?? "Windows",
                version,
                build,
                root.GetProperty("IsX64").GetBoolean(),
                sid,
                sid.EndsWith("-500", StringComparison.Ordinal),
                root.GetProperty("EnableLua").GetInt32(),
                root.GetProperty("FilterAdministratorToken").GetInt32(),
                root.GetProperty("FreeBytes").GetInt64(),
                services,
                root.GetProperty("IsCodexInstalled").GetBoolean(),
                supported);
            return OperationResult<WindowsReadinessReport>.Success(report);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return OperationResult<WindowsReadinessReport>.Failure("readiness.probe.failed", "无法解析 Windows 安装环境。");
        }
    }
}
