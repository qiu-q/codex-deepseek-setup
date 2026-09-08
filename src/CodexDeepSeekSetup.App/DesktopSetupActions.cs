using System.IO;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Diagnostics;
using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Configuration;
using CodexDeepSeekSetup.Core.DeepSeek;
using CodexDeepSeekSetup.Core.Downloads;
using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Core.Workflow;
using CodexDeepSeekSetup.Windows.Cleanup;
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
    private readonly CodexPackageVerifier payloadVerifier;
    private readonly PortableCodexInstaller portableInstaller;
    private readonly CodexCliBootstrapper cliBootstrapper;
    private readonly DeepSeekClient deepSeek;
    private readonly ISecretStore secretStore;
    private readonly CodexConfigService configService;
    private readonly IProcessRunner processRunner;
    private readonly AssistantInstallStateStore installStateStore;
    private readonly UserCleanupService userCleanupService;
    private readonly string helperSourcePath;
    private readonly string helperPath;
    private readonly string previousCodexCliPath;
    private CodexPayload? payload;
    private PortableCodexInstall? portableInstall;
    private AssistantInstallState? installState;
    private bool? codexExistedAtStart;

    public bool IsCodexInstalled { get; private set; }

    public bool HasInstallLedger => installState is not null;

    private DesktopSetupActions(
        BuildFlavorOptions flavor,
        OfficialDownloadService downloader,
        IWindowsReadinessService readiness,
        ICodexPackageManager packageManager,
        CodexPackageVerifier payloadVerifier,
        PortableCodexInstaller portableInstaller,
        CodexCliBootstrapper cliBootstrapper,
        DeepSeekClient deepSeek,
        ISecretStore secretStore,
        CodexConfigService configService,
        IProcessRunner processRunner,
        AssistantInstallStateStore installStateStore,
        UserCleanupService userCleanupService,
        string helperSourcePath,
        string helperPath,
        string previousCodexCliPath)
    {
        this.flavor = flavor;
        this.downloader = downloader;
        this.readiness = readiness;
        this.packageManager = packageManager;
        this.payloadVerifier = payloadVerifier;
        this.portableInstaller = portableInstaller;
        this.cliBootstrapper = cliBootstrapper;
        this.deepSeek = deepSeek;
        this.secretStore = secretStore;
        this.configService = configService;
        this.processRunner = processRunner;
        this.installStateStore = installStateStore;
        this.userCleanupService = userCleanupService;
        this.helperSourcePath = helperSourcePath;
        this.helperPath = helperPath;
        this.previousCodexCliPath = previousCodexCliPath;
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
        var stateStore = new AssistantInstallStateStore(Path.Combine(GetAssistantDataRoot(), "install-state.json"));
        var secretStore = new WindowsCredentialStore();
        var cleanupService = new UserCleanupService(
            secretStore,
            new WindowsUserEnvironment(),
            CleanupRoots.ForCurrentUser());
        var requestDirectory = GetRequestDirectory();
        var elevatedExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定当前安装程序路径。");
        var actions = new DesktopSetupActions(
            flavor,
            new OfficialDownloadService(new HttpClient { Timeout = TimeSpan.FromMinutes(30) }, new OfficialOriginPolicy()),
            new WindowsReadinessService(runner),
            new CodexPackageManager(verifier, runner, elevatedExecutable, requestDirectory),
            verifier,
            new PortableCodexInstaller(verifier, runner),
            new CodexCliBootstrapper(runner),
            new DeepSeekClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }),
            secretStore,
            new CodexConfigService(),
            runner,
            stateStore,
            cleanupService,
            helperSource,
            helper,
            Environment.GetEnvironmentVariable("CODEX_CLI_PATH", EnvironmentVariableTarget.User) ?? string.Empty);
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
        installState ??= await installStateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        codexExistedAtStart ??= installState?.CodexExistedBefore ?? report.IsCodexInstalled;
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

    public async Task<OperationResult<Unit>> PrepareCodexAsync(
        IProgress<SetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        payload = null;
        progress?.Report(new SetupProgress(
            "正在检查 Windows 和安装条件",
            2,
            SetupPhase.Check,
            "正在检查系统版本、账户模式、AppX 服务和系统盘空间。"));
        var check = await CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!check.IsSuccess)
        {
            return check;
        }

        progress?.Report(new SetupProgress(
            "正在准备 OpenAI 官方文件",
            8,
            SetupPhase.Download,
            "将依次准备 ChatGPT-x64.msix 和 ChatGPT-License.xml。"));
        var preparedPayload = await PreparePayloadAsync(progress, cancellationToken).ConfigureAwait(false);
        if (!preparedPayload.IsSuccess)
        {
            return Failure(preparedPayload.ErrorCode!, preparedPayload.ErrorMessage!);
        }
        var candidate = preparedPayload.Value!;

        progress?.Report(new SetupProgress(
            "正在校验官方安装文件",
            90,
            SetupPhase.Verify,
            "正在检查 MSIX 包身份、x64 架构、离线许可证和 Windows 数字签名。"));
        var verified = await payloadVerifier.VerifyAsync(candidate.MsixPath, candidate.LicensePath, cancellationToken)
            .ConfigureAwait(false);
        if (!verified.IsSuccess)
        {
            return Failure(verified.ErrorCode!, verified.ErrorMessage!);
        }

        payload = candidate;
        progress?.Report(new SetupProgress(
            "官方文件已下载并校验",
            100,
            SetupPhase.Complete,
            $"已验证 OpenAI.Codex {verified.Value!.Version} x64，可以开始安装。"));
        return OperationResult<Unit>.Success(default);
    }

    public async Task<OperationResult<Unit>> InstallPreparedCodexAsync(
        IProgress<SetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var prepared = await RevalidatePreparedPayloadAsync(progress, cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess)
        {
            return prepared;
        }

        progress?.Report(new SetupProgress(
            "等待 Windows 管理员授权",
            20,
            SetupPhase.Authorization,
            "接下来的系统弹窗要求的是 Windows 管理员密码，不是 DeepSeek API Key。"));
        var installed = await packageManager.InstallAsync(payload!.MsixPath, payload.LicensePath, cancellationToken)
            .ConfigureAwait(false);
        if (!installed.IsSuccess)
        {
            return installed;
        }

        progress?.Report(new SetupProgress(
            "Codex 系统包已部署",
            55,
            SetupPhase.Deploy,
            "离线 MSIX 和许可证已写入 Windows 应用部署服务。"));
        progress?.Report(new SetupProgress(
            "正在为当前用户注册 Codex",
            62,
            SetupPhase.Register,
            "这一步会建立开始菜单包身份和当前用户的应用注册。"));
        var registered = await packageManager.RegisterCurrentUserAsync(payload.MsixPath, cancellationToken)
            .ConfigureAwait(false);
        if (!registered.IsSuccess)
        {
            return registered;
        }

        progress?.Report(new SetupProgress(
            "正在准备 Codex CLI",
            78,
            SetupPhase.Cli,
            "正在定位官方 CLI；必要时复制同版本配套程序到当前用户目录。"));
        var cli = await cliBootstrapper.PrepareAsync(cancellationToken).ConfigureAwait(false);
        if (!cli.IsSuccess)
        {
            return Failure(cli.ErrorCode!, cli.ErrorMessage!);
        }

        IsCodexInstalled = true;
        portableInstall = null;
        progress?.Report(new SetupProgress(
            "正在保存安装记录",
            92,
            SetupPhase.SaveState,
            "只保存安装来源和清理所需路径，不保存 API Key。"));
        var stateSaved = await SaveInstallStateAsync("official", cli.Value!, null, cancellationToken)
            .ConfigureAwait(false);
        if (!stateSaved.IsSuccess)
        {
            return stateSaved;
        }
        progress?.Report(new SetupProgress(
            "Codex 安装完成",
            100,
            SetupPhase.Complete,
            "系统部署、当前用户注册和 CLI 准备均已完成。"));
        return OperationResult<Unit>.Success(default);
    }

    public async Task<OperationResult<Unit>> InstallPreparedPortableAsync(
        IProgress<SetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Failure("windows.required", "此安装助手只能在 Windows 上运行。");
        }

        progress?.Report(new SetupProgress("正在检查实验模式运行条件", 5, SetupPhase.Check));
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

        var preparedPayload = await RevalidatePreparedPayloadAsync(progress, cancellationToken).ConfigureAwait(false);
        if (!preparedPayload.IsSuccess)
        {
            return Failure(preparedPayload.ErrorCode!, preparedPayload.ErrorMessage!);
        }

        var destination = GetPortableRoot();
        var currentPayload = payload!;
        progress?.Report(new SetupProgress("正在解压已校验的 Codex", 35, SetupPhase.Deploy));
        var installed = await portableInstaller.InstallAsync(
            currentPayload.MsixPath,
            currentPayload.LicensePath,
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
        progress?.Report(new SetupProgress("正在保存实验模式记录", 90, SetupPhase.SaveState));
        var stateSaved = await SaveInstallStateAsync(
                "portable",
                portableInstall.CliPath,
                destination,
                cancellationToken)
            .ConfigureAwait(false);
        if (!stateSaved.IsSuccess)
        {
            return stateSaved;
        }
        progress?.Report(new SetupProgress("实验性 Codex 已准备完成", 100, SetupPhase.Complete));
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

        installState ??= CreateInstallState(
            codexExistedAtStart == true ? "existing" : portableInstall is null ? "official" : "portable",
            cli.Value!,
            portableInstall is null ? null : GetPortableRoot());
        installState = installState with { DeepSeekConfigured = true };
        try
        {
            await installStateStore.SaveAsync(installState, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("state.write.failed", "DeepSeek 已配置，但无法保存清理记录。请不要删除安装助手，并重试配置。");
        }

        return OperationResult<Unit>.Success(default);
    }

    public Task<OperationResult<Unit>> LaunchAsync(CancellationToken cancellationToken) =>
        portableInstall is null
            ? packageManager.LaunchAndVerifyAsync(cancellationToken)
            : LaunchPortableAsync(portableInstall, cancellationToken);

    public async Task<OperationResult<Unit>> CleanupAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Failure("windows.required", "彻底清理只能在 Windows 上执行。");
        }

        installState ??= await installStateStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        var preparedFinalizer = PrepareCleanupFinalizer();
        if (!preparedFinalizer.IsSuccess)
        {
            return Failure(preparedFinalizer.ErrorCode!, preparedFinalizer.ErrorMessage!);
        }

        var finalizerPath = preparedFinalizer.Value!;
        var finalizerStarted = false;
        try
        {
            var removed = await packageManager.RemoveAsync(cancellationToken).ConfigureAwait(false);
            if (!removed.IsSuccess)
            {
                return removed;
            }

            var previousCliPath = installState?.PreviousCodexCliPath;
            var userCleanup = await userCleanupService.CleanAsync(previousCliPath, cancellationToken)
                .ConfigureAwait(false);
            if (!userCleanup.IsSuccess)
            {
                return userCleanup;
            }

            var startInfo = new ProcessStartInfo(finalizerPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            };
            startInfo.ArgumentList.Add("finalize-cleanup");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(AppContext.BaseDirectory);
            using var finalizerProcess = Process.Start(startInfo);
            if (finalizerProcess is null)
            {
                return Failure("cleanup.finalizer.start.failed", "Codex 和用户数据已删除，但无法启动安装助手自删除程序。");
            }

            finalizerStarted = true;
            return OperationResult<Unit>.Success(default);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return Failure("cleanup.finalizer.start.failed", "Codex 和用户数据已删除，但无法启动安装助手自删除程序。");
        }
        finally
        {
            if (!finalizerStarted)
            {
                TryDeleteFile(finalizerPath);
            }
        }
    }

    private async Task<OperationResult<Unit>> RevalidatePreparedPayloadAsync(
        IProgress<SetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (payload is null || !File.Exists(payload.MsixPath) || !File.Exists(payload.LicensePath))
        {
            payload = null;
            return Failure("payload.not.prepared", "已准备的官方文件不存在，请重新下载并校验。");
        }

        progress?.Report(new SetupProgress(
            "正在安装前重新校验文件",
            8,
            SetupPhase.Verify,
            "正在确认下载完成后文件未被删除或替换。"));
        var verified = await payloadVerifier.VerifyAsync(payload.MsixPath, payload.LicensePath, cancellationToken)
            .ConfigureAwait(false);
        if (!verified.IsSuccess)
        {
            payload = null;
            return Failure("payload.revalidation.failed", "官方文件在安装前校验失败，请重新下载。");
        }

        return OperationResult<Unit>.Success(default);
    }

    private async Task<OperationResult<CodexPayload>> PreparePayloadAsync(
        IProgress<SetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (payload is not null)
        {
            return OperationResult<CodexPayload>.Success(payload);
        }

        var adjacent = FindAdjacentPayload();
        if (adjacent is not null)
        {
            progress?.Report(new SetupProgress(
                "已找到内部版本地官方文件",
                82,
                SetupPhase.Download,
                "将跳过网络下载，但仍会执行完整包身份和签名校验。"));
            return OperationResult<CodexPayload>.Success(adjacent);
        }

        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexDeepSeekSetup",
            "Downloads");
        var downloadProgress = new InlineProgress<DownloadProgress>(item =>
        {
            var filePercent = item.TotalBytes is > 0
                ? Math.Clamp(item.BytesDownloaded * 100d / item.TotalBytes.Value, 0, 100)
                : (double?)null;
            var overall = string.Equals(item.FileName, "ChatGPT-x64.msix", StringComparison.OrdinalIgnoreCase)
                ? 10 + (filePercent ?? 0) * 0.68
                : 78 + (filePercent ?? 0) * 0.08;
            progress?.Report(new SetupProgress(
                $"正在下载 {Path.GetFileName(item.FileName)}",
                Math.Clamp(overall, 0, 86),
                SetupPhase.Download,
                string.Equals(item.FileName, "ChatGPT-x64.msix", StringComparison.OrdinalIgnoreCase)
                    ? "正在接收 OpenAI 官方 Codex x64 安装包。"
                    : "正在接收与安装包匹配的官方离线许可证。",
                item.FileName,
                item.BytesDownloaded,
                item.TotalBytes,
                item.TotalBytes is null));
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

    private static string GetAssistantDataRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexDeepSeekSetup");

    private OperationResult<string> PrepareCleanupFinalizer()
    {
        if (!File.Exists(helperSourcePath))
        {
            return OperationResult<string>.Failure(
                "cleanup.finalizer.missing",
                "安装助手缺少自删除组件，请保留完整的发布目录后重试。");
        }

        var destination = Path.Combine(Path.GetTempPath(), $"CodexDeepSeekCleanup-{Guid.NewGuid():N}.exe");
        try
        {
            File.Copy(helperSourcePath, destination, overwrite: false);
            return OperationResult<string>.Success(destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteFile(destination);
            return OperationResult<string>.Failure(
                "cleanup.finalizer.copy.failed",
                "无法准备自删除组件，尚未开始清理。请检查杀毒软件或临时目录权限。");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private AssistantInstallState CreateInstallState(string mode, string cliPath, string? portableDirectory) => new(
        SchemaVersion: 1,
        CodexExistedBefore: codexExistedAtStart == true,
        InstallMode: mode,
        PreviousCodexCliPath: string.IsNullOrWhiteSpace(previousCodexCliPath) ? null : previousCodexCliPath,
        DeepSeekConfigured: false,
        DownloadCache: Path.Combine(GetAssistantDataRoot(), "Downloads"),
        PortableDirectory: portableDirectory,
        CliDirectory: Path.GetDirectoryName(cliPath),
        CredentialHelperPath: helperPath,
        AssistantDirectory: AppContext.BaseDirectory);

    private async Task<OperationResult<Unit>> SaveInstallStateAsync(
        string mode,
        string cliPath,
        string? portableDirectory,
        CancellationToken cancellationToken)
    {
        installState ??= CreateInstallState(mode, cliPath, portableDirectory);
        installState = installState with
        {
            InstallMode = mode,
            PortableDirectory = portableDirectory,
            CliDirectory = Path.GetDirectoryName(cliPath),
            AssistantDirectory = AppContext.BaseDirectory
        };
        try
        {
            await installStateStore.SaveAsync(installState, cancellationToken).ConfigureAwait(false);
            return OperationResult<Unit>.Success(default);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("state.write.failed", "Codex 已安装，但无法保存清理记录。请保留安装助手并重试。");
        }
    }

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

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
