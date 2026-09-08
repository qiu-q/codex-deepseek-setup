using System.Runtime.Versioning;
using System.Text.Json;
using CodexDeepSeekSetup.Windows.Packages;
using CodexDeepSeekSetup.Windows.Security;

namespace CodexDeepSeekSetup.Windows.Processes;

[SupportedOSPlatform("windows")]
public sealed class DefaultRestrictedOperations(
    ISecretStore secretStore,
    CodexPackageVerifier packageVerifier,
    IProcessRunner processRunner) : IRestrictedOperations
{
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
