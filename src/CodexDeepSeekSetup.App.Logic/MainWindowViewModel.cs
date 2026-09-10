using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodexDeepSeekSetup.Core.Advertisements;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.App.Logic;

public enum WizardStep
{
    Welcome,
    DeepSeek,
    Complete
}

public enum CodexInstallStage
{
    NotDownloaded,
    Downloading,
    ReadyToInstall,
    Installing,
    InstallFailed
}

public sealed record InstallDriveChoice(string RootPath, string DisplayName, bool IsDefault);

public interface IWizardActions
{
    bool IsCodexInstalled { get; }
    bool HasInstallLedger { get; }
    IReadOnlyList<InstallDriveChoice> InstallDriveChoices { get; }
    string DownloadDirectory { get; }
    string SelectedInstallDrive { get; }
    string InstallationSummary { get; }
    OperationResult<Unit> SelectDownloadDirectory(string directory);
    OperationResult<Unit> SelectInstallDrive(string driveRoot);
    Task<OperationResult<Unit>> CheckAsync(CancellationToken cancellationToken);
    Task<OperationResult<Unit>> PrepareCodexAsync(IProgress<SetupProgress>? progress, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> InstallPreparedCodexAsync(IProgress<SetupProgress>? progress, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> InstallPreparedPortableAsync(IProgress<SetupProgress>? progress, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> ValidateAndConfigureAsync(string apiKey, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> EnableBuiltInAdministratorCompatibilityAsync(CancellationToken cancellationToken);
    Task<OperationResult<Unit>> LaunchAsync(CancellationToken cancellationToken);
    Task<OperationResult<Unit>> CleanupAsync(CancellationToken cancellationToken);
}

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const int MaximumProgressEntries = 200;
    private readonly IWizardActions actions;
    private readonly IAdvertisementClient? advertisementClient;
    private readonly Func<DateTimeOffset> clock;
    private WizardStep currentStep = WizardStep.Welcome;
    private string statusMessage = "准备检查这台电脑";
    private string progressTitle = "准备开始";
    private string progressDetail = "下载和安装将分开执行，每一步都会显示结果。";
    private string currentFileName = string.Empty;
    private string transferSummary = string.Empty;
    private bool isBusy;
    private bool canUsePortable;
    private bool isProgressVisible;
    private bool isFileProgressIndeterminate;
    private double progress;
    private double fileProgress;
    private double overallProgress;
    private bool shouldExit;
    private bool needsBuiltInAdministratorCompatibility;
    private bool restartScheduled;
    private CodexInstallStage installStage = CodexInstallStage.NotDownloaded;
    private DownloadRateEstimator rateEstimator = new();
    private string? lastProgressLogKey;
    private AdCampaign? currentAdvertisement;
    private bool isAdvertisementVisible;
    private bool isCodexLaunched;

    public MainWindowViewModel(
        IWizardActions actions,
        Func<DateTimeOffset>? clock = null,
        IAdvertisementClient? advertisementClient = null)
    {
        this.actions = actions;
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.advertisementClient = advertisementClient;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WizardStep CurrentStep
    {
        get => currentStep;
        private set
        {
            if (Set(ref currentStep, value))
            {
                RaiseStepProperties();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => Set(ref statusMessage, value);
    }

    public string ProgressTitle
    {
        get => progressTitle;
        private set => Set(ref progressTitle, value);
    }

    public string ProgressDetail
    {
        get => progressDetail;
        private set => Set(ref progressDetail, value);
    }

    public string CurrentFileName
    {
        get => currentFileName;
        private set => Set(ref currentFileName, value);
    }

    public string TransferSummary
    {
        get => transferSummary;
        private set => Set(ref transferSummary, value);
    }

    public ObservableCollection<string> ProgressEntries { get; } = [];

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (Set(ref isBusy, value))
            {
                RaiseCommandProperties();
                OnPropertyChanged(nameof(CanOpenMaintenance));
            }
        }
    }

    public bool CanUsePortable
    {
        get => canUsePortable;
        private set
        {
            if (Set(ref canUsePortable, value))
            {
                OnPropertyChanged(nameof(CanRunPortable));
            }
        }
    }

    public bool IsInstallStep => CurrentStep == WizardStep.Welcome;

    public bool IsConfigureStep => CurrentStep == WizardStep.DeepSeek;

    public bool IsCompleteStep => CurrentStep == WizardStep.Complete;

    public CodexInstallStage InstallStage
    {
        get => installStage;
        private set
        {
            if (Set(ref installStage, value))
            {
                RaiseCommandProperties();
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(IsInstalling));
                OnPropertyChanged(nameof(IsReadyToInstall));
            }
        }
    }

    public bool IsDownloading => InstallStage == CodexInstallStage.Downloading;

    public bool IsInstalling => InstallStage == CodexInstallStage.Installing;

    public bool IsReadyToInstall => InstallStage is CodexInstallStage.ReadyToInstall or CodexInstallStage.InstallFailed;

    public bool CanDownload => IsInstallStep && !IsBusy;

    public bool CanInstall => IsInstallStep && !IsBusy && IsReadyToInstall;

    public bool CanConfigure => IsConfigureStep && !IsBusy;

    public bool CanLaunch => IsCompleteStep && !IsBusy;

    public bool CanCleanup => IsCompleteStep && !IsBusy && !ShouldExit;

    public AdCampaign? CurrentAdvertisement
    {
        get => currentAdvertisement;
        private set => Set(ref currentAdvertisement, value);
    }

    public bool IsAdvertisementVisible
    {
        get => isAdvertisementVisible;
        private set => Set(ref isAdvertisementVisible, value);
    }

    public bool IsCodexLaunched
    {
        get => isCodexLaunched;
        private set
        {
            if (Set(ref isCodexLaunched, value))
            {
                OnPropertyChanged(nameof(LaunchButtonText));
                OnPropertyChanged(nameof(CompletionNextStep));
            }
        }
    }

    public string LaunchButtonText => IsCodexLaunched ? "Codex 已启动" : "启动 Codex";

    public string CompletionNextStep => IsCodexLaunched
        ? "Codex 窗口已经打开。现在可以新建任务并选择 DeepSeek 模型开始使用。"
        : "下一步：点击“启动 Codex”，确认桌面窗口能够正常显示。";

    public bool CanOpenMaintenance => !IsBusy;

    public bool NeedsBuiltInAdministratorCompatibility
    {
        get => needsBuiltInAdministratorCompatibility;
        private set
        {
            if (Set(ref needsBuiltInAdministratorCompatibility, value))
            {
                OnPropertyChanged(nameof(CanEnableBuiltInAdministratorCompatibility));
            }
        }
    }

    public bool RestartScheduled
    {
        get => restartScheduled;
        private set
        {
            if (Set(ref restartScheduled, value))
            {
                OnPropertyChanged(nameof(CanEnableBuiltInAdministratorCompatibility));
            }
        }
    }

    public bool CanEnableBuiltInAdministratorCompatibility =>
        NeedsBuiltInAdministratorCompatibility && !RestartScheduled && !IsBusy;

    public bool NeedsLegacyCleanupWarning => !actions.HasInstallLedger;

    public IReadOnlyList<InstallDriveChoice> InstallDriveChoices => actions.InstallDriveChoices;

    public IReadOnlyList<DeepSeekGuideItem> DeepSeekGuideItems => DeepSeekGuideCatalog.Items;

    public string DownloadDirectory => actions.DownloadDirectory;

    public string SelectedInstallDrive
    {
        get => actions.SelectedInstallDrive;
        set => SelectInstallDrive(value);
    }

    public string InstallationSummary => actions.InstallationSummary;

    public bool ShouldExit
    {
        get => shouldExit;
        private set
        {
            if (Set(ref shouldExit, value))
            {
                OnPropertyChanged(nameof(CanCleanup));
            }
        }
    }

    public bool CanRunPortable => CanUsePortable && CanInstall;

    public bool IsProgressVisible
    {
        get => isProgressVisible;
        private set => Set(ref isProgressVisible, value);
    }

    public double Progress
    {
        get => progress;
        private set => Set(ref progress, value);
    }

    public double FileProgress
    {
        get => fileProgress;
        private set => Set(ref fileProgress, value);
    }

    public double OverallProgress
    {
        get => overallProgress;
        private set => Set(ref overallProgress, value);
    }

    public bool IsFileProgressIndeterminate
    {
        get => isFileProgressIndeterminate;
        private set => Set(ref isFileProgressIndeterminate, value);
    }

    public async Task<OperationResult<Unit>> InitializeAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            "正在检查 Windows 和 Codex…",
            "准备安装 Codex",
            actions.CheckAsync,
            cancellationToken);
        UpdateBuiltInAdministratorCompatibility(result);
        if (actions.IsCodexInstalled)
        {
            AdvanceInstalledCodexToConfiguration(
                result,
                "Codex 已安装，下一步请准备 DeepSeek API Key");
            return OperationResult<Unit>.Success(default);
        }
        if (!result.IsSuccess)
        {
            return result;
        }

        CurrentStep = WizardStep.Welcome;
        StatusMessage = "准备安装 Codex";
        RaiseActionMetadata();
        RaiseStorageProperties();
        return result;
    }

    public async Task<OperationResult<Unit>> CheckAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync("正在检查 Windows…", "检查通过，可以下载安装", actions.CheckAsync, cancellationToken);
        UpdateBuiltInAdministratorCompatibility(result);
        if (actions.IsCodexInstalled)
        {
            AdvanceInstalledCodexToConfiguration(
                result,
                "检测到 Codex 已安装，可以直接配置 DeepSeek");
            return OperationResult<Unit>.Success(default);
        }
        RaiseActionMetadata();
        RaiseStorageProperties();
        return result;
    }

    private void AdvanceInstalledCodexToConfiguration(
        OperationResult<Unit> readinessResult,
        string successMessage)
    {
        CurrentStep = WizardStep.DeepSeek;
        StatusMessage = readinessResult.IsSuccess
            ? successMessage
            : $"检测到 Codex 已安装，可以继续配置 DeepSeek。启动兼容提示：{readinessResult.ErrorMessage}";
        RaiseActionMetadata();
        RaiseStorageProperties();
    }

    private void UpdateBuiltInAdministratorCompatibility(OperationResult<Unit> result)
    {
        NeedsBuiltInAdministratorCompatibility =
            string.Equals(result.ErrorCode, "windows.builtin_admin.restricted", StringComparison.Ordinal);
        if (!NeedsBuiltInAdministratorCompatibility)
        {
            RestartScheduled = false;
        }
    }

    public async Task<OperationResult<Unit>> EnableBuiltInAdministratorCompatibilityAsync(
        CancellationToken cancellationToken)
    {
        if (!NeedsBuiltInAdministratorCompatibility)
        {
            return OperationResult<Unit>.Failure(
                "windows.builtin_admin.not_required",
                "当前账户不需要启用内置 Administrator 兼容模式。");
        }

        var result = await RunAsync(
            "正在启用内置 Administrator 兼容模式…",
            "兼容模式已启用，Windows 将在 15 秒后重启，下次登录后会自动重新打开本助手。",
            actions.EnableBuiltInAdministratorCompatibilityAsync,
            cancellationToken);
        if (result.IsSuccess)
        {
            RestartScheduled = true;
        }
        return result;
    }

    public OperationResult<Unit> SelectDownloadDirectory(string directory)
    {
        if (IsBusy)
        {
            return OperationResult<Unit>.Failure("wizard.busy", "已有操作正在进行。");
        }

        var result = actions.SelectDownloadDirectory(directory);
        if (result.IsSuccess)
        {
            OnPropertyChanged(nameof(DownloadDirectory));
            InstallStage = CodexInstallStage.NotDownloaded;
            CanUsePortable = false;
            StatusMessage = "安装包将保存到所选目录，请重新下载并校验";
        }
        else
        {
            StatusMessage = result.ErrorMessage ?? "下载目录不可用";
        }
        return result;
    }

    public OperationResult<Unit> SelectInstallDrive(string driveRoot)
    {
        if (IsBusy)
        {
            return OperationResult<Unit>.Failure("wizard.busy", "已有操作正在进行。");
        }

        var result = actions.SelectInstallDrive(driveRoot);
        if (result.IsSuccess)
        {
            OnPropertyChanged(nameof(SelectedInstallDrive));
            StatusMessage = $"Codex 将优先安装到 {driveRoot}";
        }
        else
        {
            StatusMessage = result.ErrorMessage ?? "安装磁盘不可用";
        }
        return result;
    }

    public async Task<OperationResult<Unit>> DownloadAsync(CancellationToken cancellationToken)
    {
        if (CurrentStep != WizardStep.Welcome)
        {
            return OperationResult<Unit>.Failure("wizard.order", "当前步骤不能下载。");
        }

        IsProgressVisible = true;
        Progress = 0;
        ResetTransferPresentation();
        InstallStage = CodexInstallStage.Downloading;
        CanUsePortable = false;
        var progressReporter = CreateProgressReporter();
        OperationResult<Unit> result;
        try
        {
            result = await RunAsync(
                "正在下载并校验 OpenAI 官方文件…",
                "官方文件已校验，下一步请安装 Codex",
                token => actions.PrepareCodexAsync(progressReporter, token),
                cancellationToken);
        }
        finally
        {
            Progress = 0;
            IsProgressVisible = false;
        }
        if (result.IsSuccess)
        {
            InstallStage = CodexInstallStage.ReadyToInstall;
        }
        else
        {
            InstallStage = CodexInstallStage.NotDownloaded;
        }
        return result;
    }

    public async Task<OperationResult<Unit>> InstallAsync(CancellationToken cancellationToken)
    {
        if (CurrentStep != WizardStep.Welcome || !IsReadyToInstall)
        {
            return OperationResult<Unit>.Failure("wizard.install.requires_payload", "请先下载并校验官方文件。");
        }

        IsProgressVisible = true;
        Progress = 0;
        InstallStage = CodexInstallStage.Installing;
        var progressReporter = CreateProgressReporter();
        OperationResult<Unit> result;
        try
        {
            result = await RunAsync(
                "正在准备安装 Codex…",
                "Codex 已安装，下一步请准备 DeepSeek API Key",
                token => actions.InstallPreparedCodexAsync(progressReporter, token),
                cancellationToken);
        }
        finally
        {
            Progress = 0;
            IsProgressVisible = false;
        }
        if (result.IsSuccess)
        {
            CurrentStep = WizardStep.DeepSeek;
            RaiseActionMetadata();
            RaiseStorageProperties();
        }
        else
        {
            var payloadMustBePreparedAgain = result.ErrorCode is "payload.not.prepared" or "payload.revalidation.failed";
            InstallStage = payloadMustBePreparedAgain
                ? CodexInstallStage.NotDownloaded
                : CodexInstallStage.InstallFailed;
            CanUsePortable = !payloadMustBePreparedAgain;
        }
        return result;
    }

    public async Task<OperationResult<Unit>> InstallPortableAsync(CancellationToken cancellationToken)
    {
        if (!CanUsePortable)
        {
            return OperationResult<Unit>.Failure("wizard.portable.requires_primary_failure", "请先尝试“下载并安装”；失败后才能使用实验模式。");
        }
        if (CurrentStep != WizardStep.Welcome)
        {
            return OperationResult<Unit>.Failure("wizard.order", "当前步骤不能安装。");
        }

        IsProgressVisible = true;
        Progress = 0;
        InstallStage = CodexInstallStage.Installing;
        var progressReporter = CreateProgressReporter();
        OperationResult<Unit> result;
        try
        {
            result = await RunAsync(
                "正在准备实验性 Codex…",
                "Codex 已解包，下一步请准备 DeepSeek API Key",
                token => actions.InstallPreparedPortableAsync(progressReporter, token),
                cancellationToken);
        }
        finally
        {
            Progress = 0;
            IsProgressVisible = false;
        }
        if (result.IsSuccess)
        {
            CanUsePortable = false;
            CurrentStep = WizardStep.DeepSeek;
            RaiseActionMetadata();
            RaiseStorageProperties();
        }
        else
        {
            var payloadMustBePreparedAgain = result.ErrorCode is "payload.not.prepared" or "payload.revalidation.failed";
            InstallStage = payloadMustBePreparedAgain
                ? CodexInstallStage.NotDownloaded
                : CodexInstallStage.InstallFailed;
            CanUsePortable = !payloadMustBePreparedAgain;
        }
        return result;
    }

    public async Task<OperationResult<Unit>> ConfigureAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (CurrentStep != WizardStep.DeepSeek)
        {
            return OperationResult<Unit>.Failure("wizard.order", "请先安装 Codex。");
        }

        var result = await RunAsync(
            "正在验证 API Key 并写入安全配置…",
            "配置完成，可以启动 Codex",
            token => actions.ValidateAndConfigureAsync(apiKey, token),
            cancellationToken);
        if (result.IsSuccess)
        {
            CurrentStep = WizardStep.Complete;
            RaiseActionMetadata();
        }
        return result;
    }

    public void ReturnToInstallStep()
    {
        if (IsBusy)
        {
            return;
        }

        CurrentStep = WizardStep.Welcome;
        InstallStage = CodexInstallStage.NotDownloaded;
        CanUsePortable = false;
        StatusMessage = "已返回第 1 步，可以重新测试官方文件下载";
    }

    public void RefreshExternalState()
    {
        RaiseActionMetadata();
        RaiseStorageProperties();
    }

    public async Task<OperationResult<Unit>> LaunchAsync(CancellationToken cancellationToken)
    {
        if (CurrentStep != WizardStep.Complete)
        {
            return OperationResult<Unit>.Failure("wizard.order", "请先验证 API Key 并完成配置。");
        }

        var result = await RunAsync(
            "正在启动并检查 Codex 窗口…",
            "Codex 已启动，可以开始创建任务",
            actions.LaunchAsync,
            cancellationToken);
        if (result.IsSuccess)
        {
            IsCodexLaunched = true;
        }
        return result;
    }

    public async Task LoadAdvertisementAsync(CancellationToken cancellationToken)
    {
        if (!IsCompleteStep || advertisementClient is null || IsAdvertisementVisible)
        {
            return;
        }

        var campaign = await advertisementClient.GetCurrentAsync(cancellationToken);
        if (campaign is null || !IsCompleteStep)
        {
            return;
        }

        CurrentAdvertisement = campaign;
        IsAdvertisementVisible = true;
        await advertisementClient.TrackAsync(campaign.CampaignId, AdEventType.Impression, cancellationToken);
    }

    public void DismissAdvertisement()
    {
        IsAdvertisementVisible = false;
    }

    public Uri? GetAdvertisementTarget() =>
        IsAdvertisementVisible ? CurrentAdvertisement?.TargetUrl : null;

    public async Task<Uri?> TrackAdvertisementClickAsync(CancellationToken cancellationToken)
    {
        var campaign = IsAdvertisementVisible ? CurrentAdvertisement : null;
        if (campaign is null || advertisementClient is null)
        {
            return null;
        }

        await advertisementClient.TrackAsync(campaign.CampaignId, AdEventType.Click, cancellationToken);
        return campaign.TargetUrl;
    }

    public async Task<OperationResult<Unit>> CleanupAsync(CancellationToken cancellationToken)
    {
        if (CurrentStep != WizardStep.Complete)
        {
            return OperationResult<Unit>.Failure("wizard.order", "只有完成配置后才能执行彻底清理。");
        }

        var result = await RunAsync(
            "正在移除 Codex 和当前用户数据…",
            "清理已完成，安装助手即将关闭",
            actions.CleanupAsync,
            cancellationToken);
        if (result.IsSuccess)
        {
            ShouldExit = true;
        }
        return result;
    }

    private async Task<OperationResult<Unit>> RunAsync(
        string runningMessage,
        string successMessage,
        Func<CancellationToken, Task<OperationResult<Unit>>> operation,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return OperationResult<Unit>.Failure("wizard.busy", "已有操作正在进行。");
        }

        IsBusy = true;
        StatusMessage = runningMessage;
        try
        {
            var result = await operation(cancellationToken);
            StatusMessage = result.IsSuccess ? successMessage : result.ErrorMessage ?? "操作失败";
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyProgress(SetupProgress update)
    {
        var timestamp = clock();
        StatusMessage = update.Message;
        ProgressTitle = update.Message;
        ProgressDetail = string.IsNullOrWhiteSpace(update.Detail) ? DescribePhase(update.Phase) : update.Detail;
        if (update.Percent is double percent)
        {
            var normalized = Math.Clamp(percent, 0, 100);
            Progress = normalized;
            OverallProgress = normalized;
        }

        if (!string.IsNullOrWhiteSpace(update.FileName))
        {
            CurrentFileName = update.FileName;
            var metrics = rateEstimator.Update(update, timestamp);
            IsFileProgressIndeterminate = update.IsIndeterminate || metrics.FilePercent is null;
            if (metrics.FilePercent is double filePercent)
            {
                FileProgress = filePercent;
            }
            TransferSummary = string.Join(
                " · ",
                new[] { metrics.SizeText, metrics.SpeedText, metrics.EtaText }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        else if (update.Phase != SetupPhase.Download)
        {
            CurrentFileName = string.Empty;
            TransferSummary = string.Empty;
            FileProgress = 0;
            IsFileProgressIndeterminate = update.IsIndeterminate;
        }

        AddProgressEntry(update, timestamp);
    }

    private IProgress<SetupProgress> CreateProgressReporter() =>
        new ContextProgress<SetupProgress>(SynchronizationContext.Current, ApplyProgress);

    private void ResetTransferPresentation()
    {
        rateEstimator = new DownloadRateEstimator();
        lastProgressLogKey = null;
        ProgressTitle = "正在准备下载";
        ProgressDetail = "即将连接 OpenAI 官方下载地址。";
        CurrentFileName = string.Empty;
        TransferSummary = string.Empty;
        FileProgress = 0;
        OverallProgress = 0;
        IsFileProgressIndeterminate = false;
    }

    private void AddProgressEntry(SetupProgress update, DateTimeOffset timestamp)
    {
        var key = string.Join('|', update.Phase, update.Message, update.Detail, update.FileName);
        if (string.Equals(lastProgressLogKey, key, StringComparison.Ordinal))
        {
            return;
        }

        lastProgressLogKey = key;
        var detail = string.IsNullOrWhiteSpace(update.Detail) ? string.Empty : $" — {update.Detail}";
        ProgressEntries.Add(
            $"{timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}  [{DescribePhase(update.Phase)}] {update.Message}{detail}");
        while (ProgressEntries.Count > MaximumProgressEntries)
        {
            ProgressEntries.RemoveAt(0);
        }
    }

    private static string DescribePhase(SetupPhase phase) => phase switch
    {
        SetupPhase.Check => "检查",
        SetupPhase.Download => "下载",
        SetupPhase.Verify => "校验",
        SetupPhase.Authorization => "授权",
        SetupPhase.Deploy => "部署",
        SetupPhase.Register => "注册",
        SetupPhase.Cli => "命令行组件",
        SetupPhase.SaveState => "保存状态",
        SetupPhase.Complete => "完成",
        _ => "准备"
    };

    private void RaiseStepProperties()
    {
        OnPropertyChanged(nameof(IsInstallStep));
        OnPropertyChanged(nameof(IsConfigureStep));
        OnPropertyChanged(nameof(IsCompleteStep));
        RaiseCommandProperties();
    }

    private void RaiseCommandProperties()
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanConfigure));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(CanRunPortable));
        OnPropertyChanged(nameof(CanCleanup));
        OnPropertyChanged(nameof(CanEnableBuiltInAdministratorCompatibility));
    }

    private void RaiseActionMetadata() =>
        OnPropertyChanged(nameof(NeedsLegacyCleanupWarning));

    private void RaiseStorageProperties()
    {
        OnPropertyChanged(nameof(InstallDriveChoices));
        OnPropertyChanged(nameof(SelectedInstallDrive));
        OnPropertyChanged(nameof(DownloadDirectory));
        OnPropertyChanged(nameof(InstallationSummary));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class ContextProgress<T>(SynchronizationContext? context, Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            if (context is null || ReferenceEquals(SynchronizationContext.Current, context))
            {
                handler(value);
                return;
            }

            context.Send(state => handler((T)state!), value);
        }
    }
}
