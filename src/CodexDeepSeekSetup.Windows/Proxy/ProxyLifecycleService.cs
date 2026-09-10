using System.Runtime.Versioning;
using CodexDeepSeekSetup.Core.Proxy;
using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Processes;
using CodexDeepSeekSetup.Windows.Security;
using Microsoft.Win32;

namespace CodexDeepSeekSetup.Windows.Proxy;

public sealed record ProxyLifecycleProgress(string Message, double Percent);

public sealed record ProxyPaths(
    string Root,
    string RuntimeDirectory,
    string DataDirectory,
    string ProcessStatePath,
    string RecoveryMarkerPath,
    string InstalledHelperPath)
{
    public static ProxyPaths ForCurrentUser()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "CodexDeepSeekSetup", "Network");
        return new ProxyPaths(
            root,
            Path.Combine(root, "runtime", ProxyRuntimeOptions.PinnedVersion),
            Path.Combine(root, "data"),
            Path.Combine(root, "process-state.json"),
            Path.Combine(root, "proxy-recovery.json"),
            Path.Combine(local, "Programs", "CodexDeepSeekSetup", "CodexDeepSeekSetup.NetworkHelper.exe"));
    }
}

public interface IProxyStartupRegistration
{
    OperationResult<Unit> Register(string helperPath);
    OperationResult<Unit> Unregister();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsProxyStartupRegistration : IProxyStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexDeepSeekSetupNetworkHelper";

    public OperationResult<Unit> Register(string helperPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            key.SetValue(ValueName, $"\"{Path.GetFullPath(helperPath)}\" start", RegistryValueKind.String);
            return OperationResult<Unit>.Success(default);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult<Unit>.Failure("proxy.startup.register", "无法登记网络辅助开机启动");
        }
    }

    public OperationResult<Unit> Unregister()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return OperationResult<Unit>.Success(default);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult<Unit>.Failure("proxy.startup.unregister", "无法取消网络辅助开机启动");
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class ProxyLifecycleService(
    ProxyRuntimeDownloader runtimeDownloader,
    SubscriptionImporter subscriptionImporter,
    MihomoConfigWriter configWriter,
    ProxyProcessSupervisor processSupervisor,
    WindowsUserProxyManager proxyManager,
    ISecretStore secretStore,
    IProxyStartupRegistration startupRegistration,
    ProxyPaths paths)
{
    public const int MixedPort = 17890;

    public static ProxyLifecycleService CreateDefault(HttpClient http, ProxyPaths? paths = null)
    {
        var resolved = paths ?? ProxyPaths.ForCurrentUser();
        return new ProxyLifecycleService(
            new ProxyRuntimeDownloader(http),
            new SubscriptionImporter(http),
            new MihomoConfigWriter(),
            new ProxyProcessSupervisor(new SystemProxyProcessPlatform(), resolved.ProcessStatePath),
            new WindowsUserProxyManager(new RegistryUserProxyRegistry(), new WindowsProxySettingsBroadcaster(), resolved.RecoveryMarkerPath),
            new WindowsCredentialStore(),
            new WindowsProxyStartupRegistration(),
            resolved);
    }

    public async Task<OperationResult<Unit>> EnableAsync(
        string subscriptionUrl,
        string? helperSourcePath,
        IProgress<ProxyLifecycleProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(subscriptionUrl?.Trim(), UriKind.Absolute, out var subscription) ||
            subscription.Scheme != Uri.UriSchemeHttps)
        {
            return OperationResult<Unit>.Failure("proxy.subscription.url", "请输入有效的 HTTPS 订阅地址");
        }

        progress?.Report(new("正在恢复上一次网络状态", 5));
        var stopped = await processSupervisor.StopAsync(cancellationToken).ConfigureAwait(false);
        if (!stopped.IsSuccess && stopped.ErrorCode != "proxy.process.state") return stopped;
        var restored = await proxyManager.RestoreAsync(cancellationToken).ConfigureAwait(false);
        if (!restored.IsSuccess) return restored;

        progress?.Report(new("正在从国内镜像下载并校验 Mihomo", 20));
        var runtime = await runtimeDownloader.EnsureRuntimeAsync(
            ProxyRuntimeOptions.CreateDefault(paths.RuntimeDirectory), null, cancellationToken).ConfigureAwait(false);
        if (!runtime.IsSuccess) return Failure(runtime);

        progress?.Report(new("正在安全导入订阅配置", 48));
        var providerPath = Path.Combine(paths.DataDirectory, "providers", "subscription.yaml");
        var imported = await subscriptionImporter.ImportAsync(subscription, providerPath, cancellationToken).ConfigureAwait(false);
        if (!imported.IsSuccess) return Failure(imported);

        progress?.Report(new("正在生成仅本机可用的代理配置", 62));
        var configured = configWriter.Write(paths.DataDirectory, MixedPort);
        if (!configured.IsSuccess) return Failure(configured);

        progress?.Report(new("正在启动网络辅助", 74));
        var started = await processSupervisor.StartAsync(runtime.Value!, paths.DataDirectory, MixedPort, cancellationToken).ConfigureAwait(false);
        if (!started.IsSuccess) return Failure(started);

        progress?.Report(new("正在应用当前用户代理设置", 86));
        var applied = await proxyManager.CaptureAndApplyAsync(MixedPort, cancellationToken).ConfigureAwait(false);
        if (!applied.IsSuccess)
        {
            await processSupervisor.StopAsync(CancellationToken.None).ConfigureAwait(false);
            return applied;
        }

        var credential = secretStore.Write(CredentialTargets.ProxySubscription, subscription.AbsoluteUri);
        if (!credential.IsSuccess)
        {
            await DisableAsync(deleteData: false, deleteCredential: true, deleteHelper: false, CancellationToken.None).ConfigureAwait(false);
            return OperationResult<Unit>.Failure(credential.ErrorCode!, "无法安全保存订阅地址，已恢复原网络设置");
        }

        if (!string.IsNullOrWhiteSpace(helperSourcePath))
        {
            var helperInstalled = InstallHelper(helperSourcePath);
            if (!helperInstalled.IsSuccess)
            {
                await DisableAsync(deleteData: false, deleteCredential: true, deleteHelper: false, CancellationToken.None).ConfigureAwait(false);
                return helperInstalled;
            }
            var registered = startupRegistration.Register(paths.InstalledHelperPath);
            if (!registered.IsSuccess)
            {
                await DisableAsync(deleteData: false, deleteCredential: true, deleteHelper: true, CancellationToken.None).ConfigureAwait(false);
                return registered;
            }
        }

        progress?.Report(new("网络辅助已启用，仅代理当前 Windows 用户", 100));
        return OperationResult<Unit>.Success(default);
    }

    public async Task<OperationResult<Unit>> StartSavedAsync(CancellationToken cancellationToken)
    {
        var saved = secretStore.Read(CredentialTargets.ProxySubscription);
        if (!saved.IsSuccess) return OperationResult<Unit>.Failure(saved.ErrorCode!, "没有已保存的代理订阅");
        return await EnableAsync(saved.Value!, null, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult<Unit>> DisableAsync(
        bool deleteData,
        bool deleteCredential,
        bool deleteHelper,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        var stopped = await processSupervisor.StopAsync(cancellationToken).ConfigureAwait(false);
        if (!stopped.IsSuccess && stopped.ErrorCode != "proxy.process.state") failures.Add(stopped.ErrorMessage!);
        var restored = await proxyManager.RestoreAsync(cancellationToken).ConfigureAwait(false);
        if (!restored.IsSuccess) failures.Add(restored.ErrorMessage!);
        var unregistered = startupRegistration.Unregister();
        if (!unregistered.IsSuccess) failures.Add(unregistered.ErrorMessage!);
        if (deleteCredential && !secretStore.Delete(CredentialTargets.ProxySubscription).IsSuccess) failures.Add("代理订阅凭据未删除");
        TryDelete(deleteData ? paths.Root : null, isDirectory: true, failures);
        TryDelete(deleteHelper ? paths.InstalledHelperPath : null, isDirectory: false, failures);
        return failures.Count == 0
            ? OperationResult<Unit>.Success(default)
            : OperationResult<Unit>.Failure("proxy.disable.partial", string.Join("；", failures));
    }

    private OperationResult<Unit> InstallHelper(string source)
    {
        try
        {
            if (!File.Exists(source)) return OperationResult<Unit>.Failure("proxy.helper.missing", "发布目录缺少网络辅助组件");
            Directory.CreateDirectory(Path.GetDirectoryName(paths.InstalledHelperPath)!);
            if (!Path.GetFullPath(source).Equals(Path.GetFullPath(paths.InstalledHelperPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(source, paths.InstalledHelperPath, overwrite: true);
            }
            return OperationResult<Unit>.Success(default);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return OperationResult<Unit>.Failure("proxy.helper.install", "无法安装网络辅助组件");
        }
    }

    private static OperationResult<Unit> Failure<T>(OperationResult<T> result) =>
        OperationResult<Unit>.Failure(result.ErrorCode!, result.ErrorMessage!);

    private static void TryDelete(string? path, bool isDirectory, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (isDirectory && Directory.Exists(path)) Directory.Delete(path, recursive: true);
            if (!isDirectory && File.Exists(path)) File.Delete(path);
        }
        catch { failures.Add(path); }
    }
}
