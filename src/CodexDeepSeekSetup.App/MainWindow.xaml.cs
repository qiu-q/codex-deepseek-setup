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

    protected override void OnClosed(EventArgs e)
    {
        lifetime.Cancel();
        lifetime.Dispose();
        base.OnClosed(e);
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(() => viewModel.CheckAsync(lifetime.Token));

    private async void InstallButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(() => viewModel.InstallAsync(lifetime.Token));

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
        ApiKeyBox.Clear();
        await RunUiAsync(() => viewModel.ConfigureAsync(key, lifetime.Token));
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(() => viewModel.LaunchAsync(lifetime.Token));

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private async Task RunUiAsync(Func<Task<OperationResult<Unit>>> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            MessageBox.Show(this, "操作遇到未预期错误。请重新打开程序后重试。", "安装助手", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
