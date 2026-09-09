using System.Windows;
using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.App;

public partial class MaintenanceWindow : Window
{
    private readonly MaintenanceViewModel viewModel;
    private readonly CancellationTokenSource lifetime = new();
    private bool initialized;

    public MaintenanceWindow(IMaintenanceActions actions)
    {
        InitializeComponent();
        viewModel = new MaintenanceViewModel(actions);
        DataContext = viewModel;
    }

    private async void MaintenanceWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialized)
        {
            return;
        }
        initialized = true;
        await RunUiAsync(() => viewModel.RefreshAsync(lifetime.Token));
    }

    protected override void OnClosed(EventArgs e)
    {
        lifetime.Cancel();
        lifetime.Dispose();
        base.OnClosed(e);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(() => viewModel.RefreshAsync(lifetime.Token));

    private async void MoveCodexButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            $"将由 Windows 把当前用户的 OpenAI Codex 移动到 {viewModel.SelectedInstallDrive}。不会更改其他应用的默认安装盘。继续吗？",
            "迁移 Codex",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Yes)
        {
            await RunUiAsync(() => viewModel.MoveInstalledCodexAsync(lifetime.Token));
        }
    }

    private async void CleanupSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = viewModel.Items.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "请先勾选需要删除的内容。", "没有选择", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lines = string.Join("\n", selected.Select(item => $"• {item.DisplayName}\n  {item.Location}"));
        var dangerous = selected.Any(item => item.IsDestructiveUserData);
        var message = dangerous
            ? $"以下选择包含配置、会话、应用数据、凭据或安装助手自身，删除后无法恢复：\n\n{lines}\n\n确定删除吗？"
            : $"将删除以下内容：\n\n{lines}\n\n确定继续吗？";
        var answer = MessageBox.Show(
            this,
            message,
            "确认删除已选择内容",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await RunUiAsync(() => viewModel.CleanupSelectedAsync(lifetime.Token));
        if (result?.IsSuccess == true && viewModel.ShouldExitApplication)
        {
            Application.Current.Shutdown();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task<OperationResult<Unit>?> RunUiAsync(Func<Task<OperationResult<Unit>>> operation)
    {
        try
        {
            var result = await operation();
            if (!result.IsSuccess)
            {
                MessageBox.Show(this, result.ErrorMessage, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            MessageBox.Show(this, "操作遇到未预期错误，请关闭窗口后重试。", "检测与清理", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }
}
