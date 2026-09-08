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
    Task<OperationResult<Unit>> InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> ValidateAndConfigureAsync(string apiKey, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> LaunchAsync(CancellationToken cancellationToken);
}

public sealed class MainWindowViewModel(IWizardActions actions) : INotifyPropertyChanged
{
    private WizardStep currentStep = WizardStep.Welcome;
    private string statusMessage = "准备检查这台电脑";
    private bool isBusy;
    private double progress;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WizardStep CurrentStep
    {
        get => currentStep;
        private set => Set(ref currentStep, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => Set(ref statusMessage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => Set(ref isBusy, value);
    }

    public double Progress
    {
        get => progress;
        private set => Set(ref progress, value);
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

        var progressReporter = new Progress<double>(value => Progress = value);
        var result = await RunAsync(
            "正在下载并安装官方 Codex…",
            "Codex 已安装，请创建并填写 DeepSeek API Key",
            token => actions.InstallAsync(progressReporter, token),
            cancellationToken);
        if (result.IsSuccess)
        {
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
            ? RunAsync("正在启动并检查 Codex 窗口…", "Codex 已成功显示窗口", actions.LaunchAsync, cancellationToken)
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

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
