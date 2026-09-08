using System.IO;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Diagnostics;
using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Configuration;
using CodexDeepSeekSetup.Core.DeepSeek;
using CodexDeepSeekSetup.Core.Downloads;
using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Diagnostics;
using CodexDeepSeekSetup.Windows.Packages;
using CodexDeepSeekSetup.Windows.Processes;
using CodexDeepSeekSetup.Windows.Security;

namespace CodexDeepSeekSetup.App;

[SupportedOSPlatform("windows")]
public sealed class DesktopSetupActions : IWizardActions
{
    private readonly BuildFlavorOptions flavor;
    private readonly OfficialDownloadService downloader;
    private readonly IWindowsReadinessService readiness;
    private readonly ICodexPackageManager packageManager;
    private readonly PortableCodexInstaller portableInstaller;
    private readonly CodexCliBootstrapper cliBootstrapper;
    private readonly DeepSeekClient deepSeek;
    private readonly ISecretStore secretStore;
    private readonly CodexConfigService configService;
    private readonly IProcessRunner processRunner;
    private readonly string helperSourcePath;
    private readonly string helperPath;
    private CodexPayload? payload;
    private PortableCodexInstall? portableInstall;

    public bool IsCodexInstalled { get; private set; }

    private DesktopSetupActions(
        BuildFlavorOptions flavor,
        OfficialDownloadService downloader,
        IWindowsReadinessService readiness,
        ICodexPackageManager packageManager,
        PortableCodexInstaller portableInstaller,
        CodexCliBootstrapper cliBootstrapper,
        DeepSeekClient deepSeek,
        ISecretStore secretStore,
        CodexConfigService configService,
        IProcessRunner processRunner,
        string helperSourcePath,
        string helperPath)
    {
        this.flavor = flavor;
        this.downloader = downloader;
        this.readiness = readiness;
        this.packageManager = packageManager;
        this.portableInstaller = portableInstaller;
        this.cliBootstrapper = cliBootstrapper;
        this.deepSeek = deepSeek;
        this.secretStore = secretStore;
        this.configService = configService;
        this.processRunner = processRunner;
        this.helperSourcePath = helperSourcePath;
        this.helperPath = helperPath;
    }

    public static DesktopSetupActions Create(BuildFlavorOptions flavor)
    {
        var runner = new SystemProcessRunner();
        var verifier = new CodexPackageVerifier(new PowerShellSignatureVerifier(runner));
        var helperSource = Path.Combine(AppContext.BaseDirectory, "CodexDeepSeekSetup.Helper.exe");
        var helper = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "CodexDeepSeekSetup",
            "CodexDeepSeekSetup.Helper.exe");
        var requestDirectory = GetRequestDirectory();
        var elevatedExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定当前安装程序路径。");
        var actions = new DesktopSetupActions(
            flavor,
            new OfficialDownloadService(new HttpClient { Timeout = TimeSpan.FromMinutes(30) }, new OfficialOriginPolicy()),
            new WindowsReadinessService(runner),
            new CodexPackageManager(verifier, runner, elevatedExecutable, requestDirectory),
            new PortableCodexInstaller(verifier, runner),
            new CodexCliBootstrapper(runner),
            new DeepSeekClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }),
            new WindowsCredentialStore(),
            new CodexConfigService(),
            runner,
            helperSource,
            helper);
        var portableRoot = GetPortableRoot();
        var persistedCli = Environment.GetEnvironmentVariable("CODEX_CLI_PATH", EnvironmentVariableTarget.User);
        actions.portableInstall = PortableCodexInstaller.TryRecover(portableRoot, persistedCli);
        actions.IsCodexInstalled = actions.portableInstall is not null;
        return actions;
    }

    public static string GetRequestDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
        "CodexDeepSeekSetup",
        "Requests");

    public async Task<OperationResult<Unit>> CheckAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Failure("windows.required", "此安装助手只能在 Windows 上运行。");
        }

        var result = await readiness.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        var report = result.Value!;
        if (report.IsCodexInstalled)
        {
            portableInstall = null;
        }
        IsCodexInstalled = report.IsCodexInstalled || portableInstall is not null;
        if (!report.IsSupported)
        {
            return Failure("windows.unsupported", "需要 Windows 10 内部版本 19041 或更新的 x64 系统。");
        }
        if (report.EnableLua != 1)
        {
            return Failure("windows.uac.disabled", "Windows UAC 已关闭。请先恢复 EnableLUA=1 并重启电脑。");
        }
        if (report.IsBuiltInAdministrator && report.FilterAdministratorToken != 1)
        {
            return Failure(
                "windows.builtin_admin.restricted",
                "当前是内置 Administrator（SID 末尾 -500），且管理员批准模式未开启，MSIX 应用通常无法显示窗口。请改用普通管理员账户，或启用内置管理员的管理员批准模式并重启。");
        }
        if (report.FreeSystemDriveBytes < 3L * 1024 * 1024 * 1024)
        {
            return Failure("windows.disk.low", "系统盘至少需要 3 GB 可用空间。");
        }
        var disabledService = report.Services.FirstOrDefault(service =>
            string.Equals(service.StartType, "Disabled", StringComparison.OrdinalIgnoreCase));
        if (disabledService is not null)
        {
            return Failure("windows.appx.service.disabled", $"系统服务 {disabledService.Name} 已被禁用，请恢复为默认启动类型后再试。");
        }

        return OperationResult<Unit>.Success(default);
    }

    public async Task<OperationResult<Unit>> InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var check = await CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!check.IsSuccess)
        {
            return check;
        }

        var preparedPayload = await PreparePayloadAsync(progress, cancellationToken).ConfigureAwait(false);
        if (!preparedPayload.IsSuccess)
        {
            return Failure(preparedPayload.ErrorCode!, preparedPayload.ErrorMessage!);
        }
        payload = preparedPayload.Value!;

        var installed = await packageManager.InstallAsync(payload.MsixPath, payload.LicensePath, cancellationToken)
            .ConfigureAwait(false);
        if (!installed.IsSuccess)
        {
            return installed;
        }

        var registered = await packageManager.RegisterCurrentUserAsync(payload.MsixPath, cancellationToken)
            .ConfigureAwait(false);
        if (!registered.IsSuccess)
        {
            return registered;
        }

        var cli = await cliBootstrapper.PrepareAsync(cancellationToken).ConfigureAwait(false);
        if (!cli.IsSuccess)
        {
            return Failure(cli.ErrorCode!, cli.ErrorMessage!);
        }

        IsCodexInstalled = true;
        portableInstall = null;
        return OperationResult<Unit>.Success(default);
    }

    public async Task<OperationResult<Unit>> InstallPortableAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Failure("windows.required", "此安装助手只能在 Windows 上运行。");
        }

        var readinessResult = await readiness.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!readinessResult.IsSuccess)
        {
            return Failure(readinessResult.ErrorCode!, readinessResult.ErrorMessage!);
        }
        var report = readinessResult.Value!;
        if (!report.IsSupported)
        {
            return Failure("windows.unsupported", "实验模式仍需要 Windows 10 内部版本 19041 或更新的 x64 系统。");
        }
        if (report.FreeSystemDriveBytes < 4L * 1024 * 1024 * 1024)
        {
            return Failure("windows.disk.low", "实验模式需要系统盘至少 4 GB 可用空间。");
        }

        var preparedPayload = await PreparePayloadAsync(progress, cancellationToken).ConfigureAwait(false);
        if (!preparedPayload.IsSuccess)
        {
            return Failure(preparedPayload.ErrorCode!, preparedPayload.ErrorMessage!);
        }
        payload = preparedPayload.Value!;

        var destination = GetPortableRoot();
        var installed = await portableInstaller.InstallAsync(
            payload.MsixPath,
            payload.LicensePath,
            destination,
            cancellationToken).ConfigureAwait(false);
        if (!installed.IsSuccess)
        {
            return Failure(installed.ErrorCode!, installed.ErrorMessage!);
        }

        portableInstall = installed.Value!;
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", portableInstall.CliPath, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", portableInstall.CliPath, EnvironmentVariableTarget.Process);
        IsCodexInstalled = true;
        return OperationResult<Unit>.Success(default);
    }

    public async Task<OperationResult<Unit>> ValidateAndConfigureAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (!IsCodexInstalled)
        {
            return Failure("wizard.order", "请先完成 Codex 安装。");
        }

        var cli = await PrepareCliAsync(cancellationToken).ConfigureAwait(false);
        if (!cli.IsSuccess)
        {
            return Failure(cli.ErrorCode!, cli.ErrorMessage!);
        }

        var account = await deepSeek.ValidateAsync(apiKey, cancellationToken).ConfigureAwait(false);
        if (!account.IsSuccess)
        {
            return Failure(account.ErrorCode!, account.ErrorMessage!);
        }
        if (!account.Value!.IsAvailable)
        {
            return Failure("deepseek.balance.insufficient", "DeepSeek 账户当前不可用，请先完成实名认证并充值。");
        }

        var modelTest = await deepSeek.TestResponseAsync(apiKey, "deepseek-v4-flash", cancellationToken)
            .ConfigureAwait(false);
        if (!modelTest.IsSuccess)
        {
            return Failure(modelTest.ErrorCode!, modelTest.ErrorMessage!);
        }

        var previousCredential = secretStore.Read(CredentialTargets.DeepSeekApiKey);
        if (!previousCredential.IsSuccess && previousCredential.ErrorCode != "credential.not_found")
        {
            return Failure(previousCredential.ErrorCode!, previousCredential.ErrorMessage!);
        }

        var stored = secretStore.Write(CredentialTargets.DeepSeekApiKey, apiKey);
        if (!stored.IsSuccess)
        {
            return stored;
        }

        try
        {
            if (!File.Exists(helperSourcePath))
            {
                return RestoreCredential(previousCredential).IsSuccess
                    ? Failure("install.helper.missing", "安装程序缺少权限助手，请重新解压完整发布目录。")
                    : Failure("credential.rollback.failed", "安装失败，而且原有 Windows 凭据未能恢复，请打开凭据管理器检查。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(helperPath)!);
            if (!string.Equals(Path.GetFullPath(helperSourcePath), Path.GetFullPath(helperPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(helperSourcePath, helperPath, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RestoreCredential(previousCredential).IsSuccess
                ? Failure("credential.helper.copy.failed", "无法把凭据助手保存到当前用户目录。")
                : Failure("credential.rollback.failed", "助手保存失败，而且原有 Windows 凭据未能恢复，请打开凭据管理器检查。");
        }

        var codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        OperationResult<ConfigApplyResult> configured;
        try
        {
            configured = await configService.ApplyAsync(
                new CodexConfigRequest(codexHome, "deepseek-v4-flash", helperPath, CredentialTargets.DeepSeekApiKey),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!RestoreCredential(previousCredential).IsSuccess)
            {
                throw new InvalidOperationException("配置中断后无法恢复原有 Windows 凭据。");
            }
            throw;
        }
        if (!configured.IsSuccess)
        {
            return RestoreCredential(previousCredential).IsSuccess
                ? Failure(configured.ErrorCode!, configured.ErrorMessage!)
                : Failure("credential.rollback.failed", "配置失败，而且原有 Windows 凭据未能恢复，请打开凭据管理器检查。");
        }

        ProcessResult cliCheck;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                cliCheck = await processRunner.RunAsync(
                    cli.Value!,
                    ["-a", "never", "-s", "read-only", "exec", "--ephemeral", "--skip-git-repo-check", "只回复：Codex DeepSeek 配置成功"],
                    null,
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await configService.RestoreAsync(configured.Value!, CancellationToken.None).ConfigureAwait(false);
                RestoreCredential(previousCredential);
                return Failure("codex.validation.timeout", "Codex CLI 配置验证超时，已恢复原配置和原凭据。");
            }
            catch (OperationCanceledException)
            {
                await configService.RestoreAsync(configured.Value!, CancellationToken.None).ConfigureAwait(false);
                RestoreCredential(previousCredential);
                throw;
            }
        }

        if (cliCheck.ExitCode != 0)
        {
            var restoredConfig = await configService.RestoreAsync(configured.Value!, cancellationToken).ConfigureAwait(false);
            var restoredCredential = RestoreCredential(previousCredential);
            return restoredConfig.IsSuccess && restoredCredential.IsSuccess
                ? Failure("codex.validation.failed", "Codex 未能通过新配置调用 DeepSeek，已恢复原配置和原凭据。")
                : Failure("codex.rollback.failed", "Codex 配置验证失败，自动恢复也未完全成功，请使用备份目录手动恢复。");
        }

        return OperationResult<Unit>.Success(default);
    }

    public Task<OperationResult<Unit>> LaunchAsync(CancellationToken cancellationToken) =>
        portableInstall is null
            ? packageManager.LaunchAndVerifyAsync(cancellationToken)
            : LaunchPortableAsync(portableInstall, cancellationToken);

    private async Task<OperationResult<CodexPayload>> PreparePayloadAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (payload is not null)
        {
            return OperationResult<CodexPayload>.Success(payload);
        }

        var adjacent = FindAdjacentPayload();
        if (adjacent is not null)
        {
            return OperationResult<CodexPayload>.Success(adjacent);
        }

        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexDeepSeekSetup",
            "Downloads");
        var downloadProgress = new Progress<DownloadProgress>(item =>
        {
            if (item.TotalBytes is > 0)
            {
                progress?.Report(Math.Clamp(item.BytesDownloaded * 100d / item.TotalBytes.Value, 0, 100));
            }
        });
        return await downloader.DownloadCodexPayloadAsync(cache, downloadProgress, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<OperationResult<string>> PrepareCliAsync(CancellationToken cancellationToken)
    {
        if (portableInstall is not null && File.Exists(portableInstall.CliPath))
        {
            return Task.FromResult(OperationResult<string>.Success(portableInstall.CliPath));
        }

        return cliBootstrapper.PrepareAsync(cancellationToken);
    }

    private static async Task<OperationResult<Unit>> LaunchPortableAsync(
        PortableCodexInstall install,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo(install.AppPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(install.AppPath)!
            };
            startInfo.Environment["CODEX_CLI_PATH"] = install.CliPath;
            Process.Start(startInfo);

            for (var attempt = 0; attempt < 15; attempt++)
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

            return Failure("portable.launch.window.missing", "实验性 Codex 已启动，但没有检测到窗口。此模式可能不兼容当前官方包。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Failure("portable.launch.failed", "无法从实验性目录启动 Codex。");
        }
    }

    private CodexPayload? FindAdjacentPayload()
    {
        if (!flavor.AllowAdjacentPayload)
        {
            return null;
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "payload");
        var msix = Path.Combine(directory, "ChatGPT-x64.msix");
        var license = Path.Combine(directory, "ChatGPT-License.xml");
        return File.Exists(msix) && File.Exists(license) ? new CodexPayload(msix, license) : null;
    }

    private static string GetPortableRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "OpenAI",
        "CodexPortable");

    private static OperationResult<Unit> Failure(string code, string message) =>
        OperationResult<Unit>.Failure(code, message);

    private OperationResult<Unit> RestoreCredential(OperationResult<string> previousCredential)
    {
        if (previousCredential.IsSuccess)
        {
            return secretStore.Write(CredentialTargets.DeepSeekApiKey, previousCredential.Value!);
        }

        return secretStore.Delete(CredentialTargets.DeepSeekApiKey);
    }
}
