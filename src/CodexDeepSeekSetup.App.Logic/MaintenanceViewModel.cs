using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.App.Logic;

public sealed record MaintenanceArtifactInfo(
    string Id,
    string DisplayName,
    string Location,
    long? SizeBytes,
    bool DefaultSelected,
    bool RequiresAdministrator,
    bool IsDestructiveUserData);

public sealed record MaintenanceCleanupOutcome(bool ShouldExitApplication);

public interface IMaintenanceActions
{
    IReadOnlyList<InstallDriveChoice> InstallDriveChoices { get; }
    string SelectedInstallDrive { get; }
    Task<OperationResult<IReadOnlyList<MaintenanceArtifactInfo>>> ScanAsync(CancellationToken cancellationToken);
    Task<OperationResult<Unit>> MoveInstalledCodexAsync(string driveRoot, CancellationToken cancellationToken);
    Task<OperationResult<MaintenanceCleanupOutcome>> CleanupSelectedAsync(
        IReadOnlyCollection<string> artifactIds,
        CancellationToken cancellationToken);
}

public sealed class MaintenanceArtifactItem : INotifyPropertyChanged
{
    private readonly Action selectionChanged;
    private bool isSelected;

    public MaintenanceArtifactItem(MaintenanceArtifactInfo item, Action selectionChanged)
    {
        Id = item.Id;
        DisplayName = item.DisplayName;
        Location = item.Location;
        SizeBytes = item.SizeBytes;
        RequiresAdministrator = item.RequiresAdministrator;
        IsDestructiveUserData = item.IsDestructiveUserData;
        isSelected = item.DefaultSelected;
        this.selectionChanged = selectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; }
    public string DisplayName { get; }
    public string Location { get; }
    public long? SizeBytes { get; }
    public bool RequiresAdministrator { get; }
    public bool IsDestructiveUserData { get; }
    public string SizeText => SizeBytes is long value ? FormatSize(value) : "大小未知";
    public string PermissionText => RequiresAdministrator ? "需要管理员授权" : string.Empty;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            selectionChanged();
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024 * 1024):F2} GB";
        }
        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / (1024d * 1024):F2} MB";
        }
        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:F1} KB";
        }
        return $"{bytes} B";
    }
}

public sealed class MaintenanceViewModel(IMaintenanceActions actions) : INotifyPropertyChanged
{
    private bool isBusy;
    private bool shouldExitApplication;
    private string statusMessage = "点击刷新，扫描当前用户的 Codex 数据";
    private string selectedInstallDrive = actions.SelectedInstallDrive;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<MaintenanceArtifactItem> Items { get; } = [];
    public IReadOnlyList<InstallDriveChoice> InstallDriveChoices => actions.InstallDriveChoices;

    public string SelectedInstallDrive
    {
        get => selectedInstallDrive;
        set => Set(ref selectedInstallDrive, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (Set(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanRunActions));
            }
        }
    }

    public bool CanRunActions => !IsBusy;
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public long SelectedSizeBytes => Items.Where(item => item.IsSelected).Sum(item => item.SizeBytes ?? 0);
    public string SelectedSummary => $"已选择 {SelectedCount} 项 · 已知大小 {FormatSize(SelectedSizeBytes)}";

    public string StatusMessage
    {
        get => statusMessage;
        private set => Set(ref statusMessage, value);
    }

    public bool ShouldExitApplication
    {
        get => shouldExitApplication;
        private set => Set(ref shouldExitApplication, value);
    }

    public async Task<OperationResult<Unit>> RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return OperationResult<Unit>.Failure("maintenance.busy", "已有维护操作正在进行。");
        }

        IsBusy = true;
        StatusMessage = "正在扫描 Codex 安装和当前用户数据…";
        try
        {
            var result = await actions.ScanAsync(cancellationToken);
            if (!result.IsSuccess)
            {
                StatusMessage = result.ErrorMessage ?? "扫描失败";
                return OperationResult<Unit>.Failure(result.ErrorCode!, StatusMessage);
            }

            Items.Clear();
            foreach (var item in result.Value!)
            {
                Items.Add(new MaintenanceArtifactItem(item, RaiseSelectionProperties));
            }
            RaiseSelectionProperties();
            StatusMessage = Items.Count == 0 ? "没有检测到 Codex 数据" : $"扫描完成，共发现 {Items.Count} 项";
            return OperationResult<Unit>.Success(default);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<OperationResult<Unit>> MoveInstalledCodexAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return OperationResult<Unit>.Failure("maintenance.busy", "已有维护操作正在进行。");
        }

        IsBusy = true;
        StatusMessage = $"正在把 Codex 迁移到 {SelectedInstallDrive}…";
        try
        {
            var result = await actions.MoveInstalledCodexAsync(SelectedInstallDrive, cancellationToken);
            StatusMessage = result.IsSuccess ? $"Codex 已迁移到 {SelectedInstallDrive}" : result.ErrorMessage ?? "迁移失败";
            if (result.IsSuccess)
            {
                await RefreshAfterBusyAsync(cancellationToken);
            }
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<OperationResult<Unit>> CleanupSelectedAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return OperationResult<Unit>.Failure("maintenance.busy", "已有维护操作正在进行。");
        }
        var selected = Items.Where(item => item.IsSelected).Select(item => item.Id).ToArray();
        if (selected.Length == 0)
        {
            return OperationResult<Unit>.Failure("maintenance.selection.empty", "请至少选择一项需要删除的内容。");
        }

        IsBusy = true;
        StatusMessage = "正在删除已选择的 Codex 内容…";
        try
        {
            var result = await actions.CleanupSelectedAsync(selected, cancellationToken);
            if (!result.IsSuccess)
            {
                StatusMessage = result.ErrorMessage ?? "清理未完成";
                await RefreshAfterBusyAsync(cancellationToken);
                return OperationResult<Unit>.Failure(result.ErrorCode!, StatusMessage);
            }

            ShouldExitApplication = result.Value!.ShouldExitApplication;
            StatusMessage = ShouldExitApplication ? "清理完成，安装助手即将删除自身" : "所选内容已清理";
            if (!ShouldExitApplication)
            {
                await RefreshAfterBusyAsync(cancellationToken);
            }
            return OperationResult<Unit>.Success(default);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAfterBusyAsync(CancellationToken cancellationToken)
    {
        var result = await actions.ScanAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return;
        }
        Items.Clear();
        foreach (var item in result.Value!)
        {
            Items.Add(new MaintenanceArtifactItem(item, RaiseSelectionProperties));
        }
        RaiseSelectionProperties();
    }

    private void RaiseSelectionProperties()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSizeBytes));
        OnPropertyChanged(nameof(SelectedSummary));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024 * 1024):F2} GB";
        }
        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / (1024d * 1024):F2} MB";
        }
        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:F1} KB";
        }
        return $"{bytes} B";
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
}
