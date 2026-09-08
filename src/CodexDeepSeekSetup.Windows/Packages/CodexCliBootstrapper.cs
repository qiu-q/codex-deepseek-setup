using System.Runtime.Versioning;
using System.Text.Json;
using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Processes;

namespace CodexDeepSeekSetup.Windows.Packages;

[SupportedOSPlatform("windows")]
public sealed class CodexCliBootstrapper(IProcessRunner processRunner)
{
    public async Task<OperationResult<string>> PrepareAsync(CancellationToken cancellationToken)
    {
        const string locateScript = "$p=Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop; [pscustomobject]@{InstallLocation=$p.InstallLocation;Version=$p.Version.ToString()} | ConvertTo-Json -Compress";
        var located = await processRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", locateScript],
            null,
            cancellationToken).ConfigureAwait(false);
        if (located.ExitCode != 0 || string.IsNullOrWhiteSpace(located.StandardOutput))
        {
            return OperationResult<string>.Failure("cli.package.missing", "当前用户尚未注册 Codex 安装包。");
        }

        using var packageDocument = JsonDocument.Parse(located.StandardOutput);
        var packageRoot = packageDocument.RootElement.GetProperty("InstallLocation").GetString();
        var packageVersion = packageDocument.RootElement.GetProperty("Version").GetString();
        if (string.IsNullOrWhiteSpace(packageRoot) || string.IsNullOrWhiteSpace(packageVersion))
        {
            return OperationResult<string>.Failure("cli.package.invalid", "无法读取 Codex 安装包位置和版本。");
        }

        var resources = Path.Combine(packageRoot, "app", "resources");
        var sourceCli = Path.Combine(resources, "codex.exe");
        if (!File.Exists(sourceCli))
        {
            return OperationResult<string>.Failure("cli.source.missing", "官方安装包中没有找到 Codex CLI。");
        }

        var destination = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "OpenAI",
            "Codex",
            "bin",
            packageVersion);
        Directory.CreateDirectory(destination);
        foreach (var source in Directory.EnumerateFiles(resources, "codex*.exe", SearchOption.TopDirectoryOnly))
        {
            File.Copy(source, Path.Combine(destination, Path.GetFileName(source)), overwrite: true);
        }

        var cliPath = Path.Combine(destination, "codex.exe");
        var versionCheck = await processRunner.RunAsync(cliPath, ["--version"], null, cancellationToken)
            .ConfigureAwait(false);
        if (versionCheck.ExitCode != 0)
        {
            return OperationResult<string>.Failure("cli.execution.failed", "Codex CLI 已复制，但版本检查失败。");
        }

        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", cliPath, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", cliPath, EnvironmentVariableTarget.Process);
        return OperationResult<string>.Success(cliPath);
    }
}
