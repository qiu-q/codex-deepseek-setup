using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private readonly CancellationTokenSource lifetime = new();
    private bool initialized;

    public MainWindow()
    {
        InitializeComponent();
        var flavor =
#if INTERNAL_BUILD
            BuildFlavor.Internal;
#else
            BuildFlavor.OpenSource;
#endif
        viewModel = new MainWindowViewModel(DesktopSetupActions.Create(BuildFlavorOptions.For(flavor)));
        DataContext = viewModel;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        await RunUiAsync(() => viewModel.InitializeAsync(lifetime.Token));
    }

    protected override void OnClosed(EventArgs e)
    {
        lifetime.Cancel();
        lifetime.Dispose();
        base.OnClosed(e);
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(() => viewModel.CheckAsync(lifetime.Token));

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            this,
            "将从 OpenAI 官方地址下载并安装 Codex。接下来可能出现 Windows 管理员授权窗口；请在那里输入 Windows 管理员密码，它不是 DeepSeek API Key。\n\n继续安装吗？",
            "安装官方 Codex",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);
        if (confirmed == MessageBoxResult.Yes)
        {
            await RunUiAsync(() => viewModel.InstallAsync(lifetime.Token));
        }
    }

    private async void PortableInstallButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            this,
            "实验模式不会注册 MSIX 包，只会校验并解压官方文件。自动更新、通知、协议关联和部分沙盒功能可能不可用。继续吗？",
            "实验性解包运行",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmed == MessageBoxResult.Yes)
        {
            await RunUiAsync(() => viewModel.InstallPortableAsync(lifetime.Token));
        }
    }

    private async void ConfigureButton_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password;
        var result = await RunUiAsync(() => viewModel.ConfigureAsync(key, lifetime.Token));
        if (result?.IsSuccess == true)
        {
            ApiKeyBox.Clear();
        }
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(() => viewModel.LaunchAsync(lifetime.Token));

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void CleanupEverythingButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            this,
            "此操作将永久删除：\n\n" +
            "• 当前电脑上的 OpenAI Codex 应用\n" +
            "• 整个 %USERPROFILE%\\.codex（包括配置、插件缓存和会话）\n" +
            "• DeepSeek API Key 的 Windows 凭据\n" +
            "• 本助手创建的下载缓存、CLI、便携目录和安装记录\n" +
            "• 本安装助手自身\n\n" +
            "删除后无法恢复。是否继续？",
            "彻底卸载并清理",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        if (viewModel.NeedsLegacyCleanupWarning)
        {
            var legacyConfirmed = MessageBox.Show(
                this,
                "没有找到完整的安装记录，无法确认 Codex 是否在使用本助手前已存在。\n\n本次仍会删除检测到的官方 Codex 和整个 .codex 目录。确定继续吗？",
                "再次确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (legacyConfirmed != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var result = await RunUiAsync(() => viewModel.CleanupAsync(lifetime.Token));
        if (result?.IsSuccess == true && viewModel.ShouldExit)
        {
            Application.Current.Shutdown();
        }
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private async Task<OperationResult<Unit>?> RunUiAsync(Func<Task<OperationResult<Unit>>> operation)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            MessageBox.Show(this, "操作遇到未预期错误。请重新打开程序后重试。", "安装助手", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }
}
