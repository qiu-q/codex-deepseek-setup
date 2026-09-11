using System.IO;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Diagnostics;
using System.ComponentModel;
using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Configuration;
using CodexDeepSeekSetup.Core.DeepSeek;
using CodexDeepSeekSetup.Core.Downloads;
using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Core.Proxy;
using CodexDeepSeekSetup.Core.Workflow;
using CodexDeepSeekSetup.Windows.Cleanup;
using CodexDeepSeekSetup.Windows.Diagnostics;
using CodexDeepSeekSetup.Windows.Packages;
using CodexDeepSeekSetup.Windows.Processes;
using CodexDeepSeekSetup.Windows.Proxy;
using CodexDeepSeekSetup.Windows.Security;
using CodexDeepSeekSetup.Windows.Storage;

namespace CodexDeepSeekSetup.App;

[SupportedOSPlatform("windows")]
public sealed class DesktopSetupActions : IWizardActions, IMaintenanceActions, INetworkNodeActions
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
    private readonly CodexArtifactInventory artifactInventory;
    private readonly SelectiveCleanupService selectiveCleanupService;
    private readonly string helperSourcePath;
    private readonly string helperPath;
    private readonly string previousCodexCliPath;
    private readonly string elevatedExecutablePath;
    private readonly ProxyLifecycleService? proxyLifecycle;
    private readonly string networkHelperSourcePath;
    private CodexPayload? payload;
    private PortableCodexInstall? portableInstall;
    private AssistantInstallState? installState;
    private bool? codexExistedAtStart;
    private CodexPackageStatus packageStatus = CodexPackageStatus.NotInstalled;
    private string downloadDirectory;
    private string selectedInstallDrive;
    private bool downloadDirectoryWasSelected;

    public IReadOnlyList<InstallDriveChoice> InstallDriveChoices { get; }

    public string DownloadDirectory => downloadDirectory;

    public string SelectedInstallDrive => selectedInstallDrive;

    public string InstallationSummary => packageStatus.IsInstalled
        ? $"已检测到 OpenAI Codex {packageStatus.Version} · {packageStatus.DriveRoot} · {packageStatus.InstallLocation}"
        : portableInstall is not null
            ? $"已检测到实验性 Codex · {portableInstall.AppPath}"
            : "未检测到 Codex 安装";

    public bool IsCodexInstalled { get; private set; }

    public bool HasInstallLedger => installState is not null;

    public bool IsNetworkHelperAvailable => flavor.EnableProxyConfiguration;

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
        CodexArtifactInventory artifactInventory,
        SelectiveCleanupService selectiveCleanupService,
        StorageSelectionService storageSelectionService,
        string helperSourcePath,
        string helperPath,
        string previousCodexCliPath,
        string elevatedExecutablePath,
        ProxyLifecycleService? proxyLifecycle,
        string networkHelperSourcePath)
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
        this.artifactInventory = artifactInventory;
        this.selectiveCleanupService = selectiveCleanupService;
        this.helperSourcePath = helperSourcePath;
        this.helperPath = helperPath;
        this.previousCodexCliPath = previousCodexCliPath;
        this.elevatedExecutablePath = elevatedExecutablePath;
        this.proxyLifecycle = proxyLifecycle;
        this.networkHelperSourcePath = networkHelperSourcePath;
        var driveOptions = storageSelectionService.GetInstallDrives();
        InstallDriveChoices = driveOptions
            .Select(option => new InstallDriveChoice(option.RootPath, option.DisplayName, option.IsDefault))
            .ToArray();
        selectedInstallDrive = InstallDriveChoices.FirstOrDefault(choice => choice.IsDefault)?.RootPath
            ?? InstallDriveChoices.FirstOrDefault()?.RootPath
            ?? Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System))
            ?? @"C:\";
        downloadDirectory = StorageSelectionService.ResolveDefaultDownloadDirectory(
            AppContext.BaseDirectory,
            Path.Combine(GetAssistantDataRoot(), "Downloads"));
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
        var cleanupRoots = CleanupRoots.ForCurrentUser();
        var userEnvironment = new WindowsUserEnvironment();
        var cleanupService = new UserCleanupService(
            secretStore,
            userEnvironment,
            cleanupRoots);
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
            new CodexArtifactInventory(cleanupRoots),
            new SelectiveCleanupService(secretStore, userEnvironment, cleanupRoots),
            new StorageSelectionService(),
            helperSource,
            helper,
            Environment.GetEnvironmentVariable("CODEX_CLI_PATH", EnvironmentVariableTarget.User) ?? string.Empty,
            elevatedExecutable,
            flavor.EnableProxyConfiguration
                ? ProxyLifecycleService.CreateDefault(new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
                : null,
            Path.Combine(AppContext.BaseDirectory, "CodexDeepSeekSetup.NetworkHelper.exe"));
        var portableRoot = GetPortableRoot();
        var persistedCli = Environment.GetEnvironmentVariable("CODEX_CLI_PATH", EnvironmentVariableTarget.User);
        actions.portableInstall = PortableCodexInstaller.TryRecover(portableRoot, persistedCli);
        actions.IsCodexInstalled = actions.portableInstall is not null;
        return actions;
    }

    public Task<OperationResult<Unit>> EnableNetworkHelperAsync(
        string subscriptionUrl,
        IProgress<ProxyLifecycleProgress>? progress,
        CancellationToken cancellationToken) =>
        proxyLifecycle is null
            ? Task.FromResult(Failure("proxy.disabled", "开源版不包含网络辅助功能"))
            : proxyLifecycle.EnableAsync(subscriptionUrl, networkHelperSourcePath, progress, cancellationToken);

    public Task<OperationResult<Unit>> DisableNetworkHelperAsync(CancellationToken cancellationToken) =>
        proxyLifecycle is null
            ? Task.FromResult(OperationResult<Unit>.Success(default))
            : proxyLifecycle.DisableAsync(true, true, true, cancellationToken);

    public Task<OperationResult<IReadOnlyList<ProxyNode>>> GetNetworkNodesAsync(CancellationToken cancellationToken) =>
        proxyLifecycle is null
            ? Task.FromResult(OperationResult<IReadOnlyList<ProxyNode>>.Failure("proxy.disabled", "网络辅助功能不可用"))
            : proxyLifecycle.GetNodesAsync(cancellationToken);

    public Task<OperationResult<int>> GetNetworkNodeDelayAsync(string nodeName, CancellationToken cancellationToken) =>
        proxyLifecycle is null
            ? Task.FromResult(OperationResult<int>.Failure("proxy.disabled", "网络辅助功能不可用"))
            : proxyLifecycle.GetNodeDelayAsync(nodeName, cancellationToken);

    public Task<OperationResult<Unit>> SelectNetworkNodeAsync(string nodeName, CancellationToken cancellationToken) =>
        proxyLifecycle is null
            ? Task.FromResult(Failure("proxy.disabled", "网络辅助功能不可用"))
            : proxyLifecycle.SelectNodeAsync(nodeName, cancellationToken);

    public async Task<OperationResult<IReadOnlyList<MaintenanceArtifactInfo>>> ScanAsync(
        CancellationToken cancellationToken)
    {
        installState ??= await installStateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!downloadDirectoryWasSelected && !string.IsNullOrWhiteSpace(installState?.DownloadCache))
        {
            try
            {
                downloadDirectory = Path.GetFullPath(installState.DownloadCache);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
            }
        }
        var status = await packageManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsSuccess)
        {
            return OperationResult<IReadOnlyList<MaintenanceArtifactInfo>>.Failure(status.ErrorCode!, status.ErrorMessage!);
        }
        packageStatus = status.Value!;
        IsCodexInstalled = packageStatus.IsInstalled || portableInstall is not null;

        var credential = secretStore.Exists(CredentialTargets.DeepSeekApiKey);
        if (!credential.IsSuccess)
        {
            return OperationResult<IReadOnlyList<MaintenanceArtifactInfo>>.Failure(
                credential.ErrorCode!,
                credential.ErrorMessage!);
        }

        var cliPath = Environment.GetEnvironmentVariable("CODEX_CLI_PATH", EnvironmentVariableTarget.User);
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME", EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable("CODEX_HOME", EnvironmentVariableTarget.User);
        var artifacts = await artifactInventory.ScanAsync(
            downloadDirectory,
            packageStatus,
            credential.Value,
            cliPath,
            codexHome,
            cancellationToken).ConfigureAwait(false);
        var items = artifacts.Select(MapArtifact).ToList();
        if (proxyLifecycle is not null)
        {
            var proxyPaths = ProxyPaths.ForCurrentUser();
            var proxyCredential = secretStore.Exists(CredentialTargets.ProxySubscription);
            if (Directory.Exists(proxyPaths.Root) || proxyCredential is { IsSuccess: true, Value: true })
            {
                items.Add(new MaintenanceArtifactInfo(
                    CodexArtifactIds.NetworkHelper,
                    "Mihomo 网络辅助、订阅凭据与代理恢复记录",
                    proxyPaths.Root,
                    TryGetDirectorySize(proxyPaths.Root),
                    true,
                    false,
                    false));
            }
        }
        var bundledPayloadDirectory = Path.Combine(AppContext.BaseDirectory, "payload");
        var bundledPayloadFiles = SelfDeleteFinalizer.BundledPayloadFileNames
            .Select(name => Path.Combine(bundledPayloadDirectory, name))
            .Where(File.Exists)
            .ToArray();
        if (bundledPayloadFiles.Length > 0)
        {
            items.Add(new MaintenanceArtifactInfo(
                CodexArtifactIds.BundledPayload,
                "随助手附带的官方安装文件",
                bundledPayloadDirectory,
                bundledPayloadFiles.Sum(TryGetFileSize),
                true,
                false,
                false));
        }
        if (SelfDeleteFinalizer.PublishedFileNames.Any(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name))))
        {
            long size = 0;
            foreach (var name in SelfDeleteFinalizer.PublishedFileNames)
            {
                var path = Path.Combine(AppContext.BaseDirectory, name);
                if (File.Exists(path))
                {
                    size += TryGetFileSize(path);
                }
            }
            items.Add(new MaintenanceArtifactInfo(
                CodexArtifactIds.AssistantSelf,
                "本安装助手",
                AppContext.BaseDirectory,
                size,
                false,
                false,
                true));
        }
        return OperationResult<IReadOnlyList<MaintenanceArtifactInfo>>.Success(items);
    }

    public async Task<OperationResult<Unit>> MoveInstalledCodexAsync(
        string driveRoot,
        CancellationToken cancellationToken)
    {
        var selected = SelectInstallDrive(driveRoot);
        if (!selected.IsSuccess)
        {
            return selected;
        }
        var moved = await packageManager.MoveCurrentUserPackageAsync(driveRoot, cancellationToken).ConfigureAwait(false);
        if (!moved.IsSuccess)
        {
            return moved;
        }
        var status = await packageManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.IsSuccess)
        {
            packageStatus = status.Value!;
        }
        return moved;
    }

    public async Task<OperationResult<MaintenanceCleanupOutcome>> CleanupSelectedAsync(
        IReadOnlyCollection<string> artifactIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifactIds);
        var selected = artifactIds.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            return OperationResult<MaintenanceCleanupOutcome>.Failure(
                "maintenance.selection.empty",
                "请至少选择一项需要删除的内容。");
        }

        string? finalizerPath = null;
        if (selected.Contains(CodexArtifactIds.AssistantSelf))
        {
            var prepared = PrepareCleanupFinalizer();
            if (!prepared.IsSuccess)
            {
                return OperationResult<MaintenanceCleanupOutcome>.Failure(prepared.ErrorCode!, prepared.ErrorMessage!);
            }
            finalizerPath = prepared.Value!;
        }

        try
        {
            var failures = new List<string>();
            if (proxyLifecycle is not null && selected.Overlaps(
                [CodexArtifactIds.NetworkHelper, CodexArtifactIds.AssistantData, CodexArtifactIds.AssistantInstall, CodexArtifactIds.AssistantSelf]))
            {
                var proxyCleanup = await proxyLifecycle.DisableAsync(true, true, true, cancellationToken).ConfigureAwait(false);
                if (!proxyCleanup.IsSuccess) failures.Add(proxyCleanup.ErrorMessage ?? "网络辅助未能完全清理");
            }
            if (selected.Overlaps(
                [
                    CodexArtifactIds.Package,
                    CodexArtifactIds.AppLocalData,
                    CodexArtifactIds.DesktopRuntime,
                    CodexArtifactIds.Cli,
                    CodexArtifactIds.Portable
                ]))
            {
                await processRunner.RunAsync(
                    "powershell.exe",
                    ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Get-Process -Name ChatGPT,Codex -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue"],
                    null,
                    cancellationToken).ConfigureAwait(false);
            }

            if (selected.Contains(CodexArtifactIds.Package))
            {
                var removed = await packageManager.RemoveAsync(cancellationToken).ConfigureAwait(false);
                if (!removed.IsSuccess)
                {
                    failures.Add(removed.ErrorMessage ?? "OpenAI Codex 系统包未能删除");
                }
                else
                {
                    packageStatus = CodexPackageStatus.NotInstalled;
                    IsCodexInstalled = portableInstall is not null;
                }
            }

            var userSelection = selected
                .Where(id => id is not CodexArtifactIds.Package and not CodexArtifactIds.AssistantSelf and not CodexArtifactIds.BundledPayload)
                .ToArray();
            var cleaned = await selectiveCleanupService.CleanAsync(
                userSelection,
                downloadDirectory,
                installState?.PreviousCodexCliPath,
                Environment.GetEnvironmentVariable("CODEX_HOME", EnvironmentVariableTarget.Process)
                    ?? Environment.GetEnvironmentVariable("CODEX_HOME", EnvironmentVariableTarget.User),
                cancellationToken).ConfigureAwait(false);
            if (!cleaned.IsSuccess)
            {
                failures.Add(cleaned.ErrorMessage ?? "部分当前用户数据未能删除");
            }
            if (selected.Contains(CodexArtifactIds.Portable) && !Directory.Exists(GetPortableRoot()))
            {
                portableInstall = null;
            }
            IsCodexInstalled = packageStatus.IsInstalled || portableInstall is not null;

            if (selected.Contains(CodexArtifactIds.BundledPayload))
            {
                var bundledCleanup = DeleteBundledPayloadFiles();
                if (!bundledCleanup.IsSuccess)
                {
                    failures.Add(bundledCleanup.ErrorMessage ?? "随助手附带的官方安装文件未能删除");
                }
            }

            if (failures.Count > 0)
            {
                return OperationResult<MaintenanceCleanupOutcome>.Failure(
                    "cleanup.selected.partial",
                    "部分内容已经处理，但以下项目未完成：" + string.Join("；", failures));
            }

            if (finalizerPath is null)
            {
                return OperationResult<MaintenanceCleanupOutcome>.Success(new MaintenanceCleanupOutcome(false));
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
            using var finalizer = Process.Start(startInfo);
            if (finalizer is null)
            {
                return OperationResult<MaintenanceCleanupOutcome>.Failure(
                    "cleanup.finalizer.start.failed",
                    "所选内容已删除，但无法启动安装助手自删除程序。");
            }

            finalizerPath = null;
            return OperationResult<MaintenanceCleanupOutcome>.Success(new MaintenanceCleanupOutcome(true));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(finalizerPath))
            {
                TryDeleteFile(finalizerPath);
            }
        }
    }

    private static MaintenanceArtifactInfo MapArtifact(CodexArtifact artifact) => new(
        artifact.Id,
        artifact.DisplayName,
        artifact.Location,
        artifact.SizeBytes,
        artifact.DefaultSelected,
        artifact.RequiresAdministrator,
        artifact.IsDestructiveUserData);

    private static long TryGetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static long? TryGetDirectorySize(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(TryGetFileSize)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static OperationResult<Unit> DeleteBundledPayloadFiles()
    {
        var payloadDirectory = Path.Combine(AppContext.BaseDirectory, "payload");
        try
        {
            foreach (var name in SelfDeleteFinalizer.BundledPayloadFileNames)
            {
                var path = Path.Combine(payloadDirectory, name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            if (Directory.Exists(payloadDirectory) && !Directory.EnumerateFileSystemEntries(payloadDirectory).Any())
            {
                Directory.Delete(payloadDirectory);
            }
            return OperationResult<Unit>.Success(default);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("cleanup.bundled_payload.failed", "无法删除随助手附带的官方安装文件，请关闭占用文件的程序后重试。");
        }
    }

    public OperationResult<Unit> SelectDownloadDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Failure("download.directory.empty", "请选择安装包保存目录。");
        }

        try
        {
            var fullPath = Path.GetFullPath(directory);
            Directory.CreateDirectory(fullPath);
            var probe = Path.Combine(fullPath, $".codex-setup-write-test-{Guid.NewGuid():N}");
            using (File.Create(probe))
            {
            }
            File.Delete(probe);
            downloadDirectory = fullPath;
            downloadDirectoryWasSelected = true;
            payload = null;
            return OperationResult<Unit>.Success(default);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure("download.directory.unwritable", "所选目录不可写，请换一个目录。");
        }
    }

    public OperationResult<Unit> SelectInstallDrive(string driveRoot)
    {
        var choice = InstallDriveChoices.FirstOrDefault(item =>
            string.Equals(item.RootPath, driveRoot, StringComparison.OrdinalIgnoreCase));
        if (choice is null)
        {
            return Failure("install.drive.unavailable", "所选磁盘不是可用的本地固定 NTFS 磁盘，或剩余空间不足 3 GB。");
        }

        selectedInstallDrive = choice.RootPath;
        return OperationResult<Unit>.Success(default);
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
        var packageStatusResult = await packageManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!packageStatusResult.IsSuccess)
        {
            return Failure(packageStatusResult.ErrorCode!, packageStatusResult.ErrorMessage!);
        }
        packageStatus = packageStatusResult.Value!;
        installState ??= await installStateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!downloadDirectoryWasSelected && !string.IsNullOrWhiteSpace(installState?.DownloadCache))
        {
            try
            {
                downloadDirectory = Path.GetFullPath(installState.DownloadCache);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
            }
        }
        codexExistedAtStart ??= installState?.CodexExistedBefore ?? report.IsCodexInstalled;
        if (packageStatus.IsInstalled)
        {
            portableInstall = null;
        }
        IsCodexInstalled = packageStatus.IsInstalled || portableInstall is not null;
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

    public async Task<OperationResult<Unit>> EnableBuiltInAdministratorCompatibilityAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(elevatedExecutablePath))
        {
            return Failure(
                "windows.builtin_admin.relaunch_missing",
                "找不到当前安装助手，无法设置重启后自动重开。");
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
            startInfo.ArgumentList.Add("enable-builtin-admin-compatibility");
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Failure("windows.builtin_admin.elevation_failed", "无法启动 Windows 管理员授权。");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0
                ? OperationResult<Unit>.Success(default)
                : Failure(
                    "windows.builtin_admin.enable_failed",
                    $"启用兼容模式失败（退出码 {process.ExitCode}）。请确认 UAC 已启用，然后重试。");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return Failure("windows.builtin_admin.elevation_cancelled", "已取消 Windows 管理员授权，未修改系统设置。");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return Failure("windows.builtin_admin.enable_failed", "无法启用内置 Administrator 兼容模式。");
        }
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

        var moveWarning = string.Empty;
        var statusAfterRegistration = await packageManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (statusAfterRegistration.IsSuccess)
        {
            packageStatus = statusAfterRegistration.Value!;
        }
        if (!string.IsNullOrWhiteSpace(selectedInstallDrive) &&
            packageStatus.IsInstalled &&
            !string.Equals(packageStatus.DriveRoot, selectedInstallDrive, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(new SetupProgress(
                $"正在把 Codex 移到 {selectedInstallDrive}",
                70,
                SetupPhase.Deploy,
                "只移动 OpenAI.Codex，不改变 Windows 其他应用的默认安装盘。"));
            var moved = await packageManager.MoveCurrentUserPackageAsync(selectedInstallDrive, cancellationToken)
                .ConfigureAwait(false);
            if (!moved.IsSuccess)
            {
                moveWarning = moved.ErrorMessage ?? "目标磁盘迁移失败，Codex 已保留在原磁盘。";
            }
            else
            {
                var movedStatus = await packageManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                if (movedStatus.IsSuccess)
                {
                    packageStatus = movedStatus.Value!;
                }
            }
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
            string.IsNullOrWhiteSpace(moveWarning)
                ? $"系统部署、当前用户注册和 CLI 准备均已完成。安装盘：{packageStatus.DriveRoot}"
                : $"Codex 已安装，但磁盘迁移未完成：{moveWarning}"));
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

        OperationResult<string> cli;
        using (var cliTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cliTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                cli = await PrepareCliAsync(cliTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Failure("cli.prepare.timeout", "检查 Codex CLI 超时。请关闭正在运行的 Codex 后重试。");
            }
        }
        if (!cli.IsSuccess)
        {
            return Failure(cli.ErrorCode!, cli.ErrorMessage!);
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
            if (proxyLifecycle is not null)
            {
                var proxyCleanup = await proxyLifecycle.DisableAsync(true, true, true, cancellationToken).ConfigureAwait(false);
                if (!proxyCleanup.IsSuccess) return proxyCleanup;
            }
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

        var cache = downloadDirectory;
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
        DownloadCache: downloadDirectory,
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
            DownloadCache = downloadDirectory,
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
