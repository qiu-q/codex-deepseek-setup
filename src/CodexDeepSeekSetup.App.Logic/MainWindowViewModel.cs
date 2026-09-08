using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.App.Logic;

public enum WizardStep
{
    Welcome,
    DeepSeek,
    Complete
}

public interface IWizardActions
{
    bool IsCodexInstalled { get; }
    Task<OperationResult<Unit>> CheckAsync(CancellationToken cancellationToken);
    Task<OperationResult<Unit>> InstallAsync(IProgress<SetupProgress>? progress, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> InstallPortableAsync(IProgress<SetupProgress>? progress, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> ValidateAndConfigureAsync(string apiKey, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> LaunchAsync(CancellationToken cancellationToken);
}

public sealed class MainWindowViewModel(IWizardActions actions) : INotifyPropertyChanged
{
    private WizardStep currentStep = WizardStep.Welcome;
    private string statusMessage = "准备检查这台电脑";
    private bool isBusy;
    private bool canUsePortable;
    private bool isProgressVisible;
    private double progress;

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

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (Set(ref isBusy, value))
            {
                RaiseCommandProperties();
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

    public bool CanInstall => IsInstallStep && !IsBusy;

    public bool CanConfigure => IsConfigureStep && !IsBusy;

    public bool CanLaunch => IsCompleteStep && !IsBusy;

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

    public async Task<OperationResult<Unit>> InitializeAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            "正在检查 Windows 和 Codex…",
            "准备安装 Codex",
            actions.CheckAsync,
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result;
        }

        if (actions.IsCodexInstalled)
        {
            CurrentStep = WizardStep.DeepSeek;
            StatusMessage = "Codex 已安装，下一步请准备 DeepSeek API Key";
        }
        else
        {
            CurrentStep = WizardStep.Welcome;
            StatusMessage = "准备安装 Codex";
        }
        return result;
    }

    public async Task<OperationResult<Unit>> CheckAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync("正在检查 Windows…", "检查通过，可以下载安装", actions.CheckAsync, cancellationToken);
        if (result.IsSuccess && actions.IsCodexInstalled)
        {
            CurrentStep = WizardStep.DeepSeek;
            StatusMessage = "检测到 Codex 已安装，可以直接配置 DeepSeek";
        }
        return result;
    }

    public async Task<OperationResult<Unit>> InstallAsync(CancellationToken cancellationToken)
    {
        if (CurrentStep != WizardStep.Welcome)
        {
            return OperationResult<Unit>.Failure("wizard.order", "当前步骤不能安装。");
        }

        IsProgressVisible = true;
        Progress = 0;
        var progressReporter = new InlineProgress<SetupProgress>(ApplyProgress);
        OperationResult<Unit> result;
        try
        {
            result = await RunAsync(
                "正在准备安装官方 Codex…",
                "Codex 已安装，下一步请准备 DeepSeek API Key",
                token => actions.InstallAsync(progressReporter, token),
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
        }
        else
        {
            CanUsePortable = true;
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
        var progressReporter = new InlineProgress<SetupProgress>(ApplyProgress);
        OperationResult<Unit> result;
        try
        {
            result = await RunAsync(
                "正在准备实验性 Codex…",
                "Codex 已解包，下一步请准备 DeepSeek API Key",
                token => actions.InstallPortableAsync(progressReporter, token),
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
        }
        return result;
    }

    public Task<OperationResult<Unit>> LaunchAsync(CancellationToken cancellationToken) =>
        CurrentStep == WizardStep.Complete
            ? RunAsync("正在启动并检查 Codex 窗口…", "Codex 已启动", actions.LaunchAsync, cancellationToken)
            : Task.FromResult(OperationResult<Unit>.Failure("wizard.order", "请先验证 API Key 并完成配置。"));

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
        StatusMessage = update.Message;
        if (update.Percent is double percent)
        {
            Progress = Math.Clamp(percent, 0, 100);
        }
    }

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
        OnPropertyChanged(nameof(CanConfigure));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(CanRunPortable));
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

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
