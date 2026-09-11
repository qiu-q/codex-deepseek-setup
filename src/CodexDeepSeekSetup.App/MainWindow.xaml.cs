using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Advertisements;
using CodexDeepSeekSetup.Core.Guides;
using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Proxy;

namespace CodexDeepSeekSetup.App;

public partial class MainWindow : Window
{
    private readonly DesktopSetupActions actions;
    private readonly MainWindowViewModel viewModel;
    private readonly HttpClient? advertisementHttp;
    private readonly AdvertisementImageDownloader? advertisementImageDownloader;
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
        var options = BuildFlavorOptions.For(flavor);
        actions = DesktopSetupActions.Create(options);
        NetworkHelperCard.Visibility = options.EnableProxyConfiguration ? Visibility.Visible : Visibility.Collapsed;
        IAdvertisementClient? advertisementClient = null;
        IGuideClient? guideClient = null;
        if (options.AdvertisementEndpoint is not null || options.GuideEndpoint is not null)
        {
            advertisementHttp = new HttpClient();
            advertisementImageDownloader = new AdvertisementImageDownloader(advertisementHttp);
            if (options.AdvertisementEndpoint is not null)
            {
                advertisementClient = new AdvertisementClient(advertisementHttp, options.AdvertisementEndpoint);
            }
            if (options.GuideEndpoint is not null)
            {
                guideClient = new GuideClient(advertisementHttp, options.GuideEndpoint);
            }
        }
        viewModel = new MainWindowViewModel(actions, advertisementClient: advertisementClient, guideClient: guideClient);
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
        await ShowGuideIfNeededAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        lifetime.Cancel();
        lifetime.Dispose();
        advertisementHttp?.Dispose();
        base.OnClosed(e);
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiAsync(() => viewModel.CheckAsync(lifetime.Token));
        await ShowGuideIfNeededAsync();
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(() => viewModel.DownloadAsync(lifetime.Token));

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            this,
            "将使用已经下载并校验通过的官方文件安装 Codex。接下来可能出现 Windows 管理员授权窗口；请在那里输入 Windows 管理员密码，它不是 DeepSeek API Key。\n\n安装失败后可以直接重试，不会重新下载。继续吗？",
            "安装官方 Codex",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);
        if (confirmed == MessageBoxResult.Yes)
        {
            await RunUiAsync(() => viewModel.InstallAsync(lifetime.Token));
            await ShowGuideIfNeededAsync();
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
            await ShowGuideIfNeededAsync();
        }
    }

    private async void ConfigureButton_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password;
        var result = await RunUiAsync(() => viewModel.ConfigureAsync(key, lifetime.Token));
        if (result?.IsSuccess == true)
        {
            ApiKeyBox.Clear();
            await viewModel.LoadAdvertisementAsync(lifetime.Token);
            await LoadAdvertisementImageAsync();
        }
        else if (result is { ErrorMessage: not null })
        {
            MessageBox.Show(this, result.ErrorMessage, "DeepSeek 配置未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EnableNetworkHelperButton_Click(object sender, RoutedEventArgs e)
    {
        var subscription = SubscriptionUrlBox.Password;
        SetNetworkButtons(false);
        NetworkHelperProgressBar.Value = 0;
        var progress = new Progress<ProxyLifecycleProgress>(update =>
        {
            NetworkHelperProgressBar.Value = update.Percent;
            NetworkHelperStatusText.Text = update.Message;
        });
        var result = await RunUiAsync(() => actions.EnableNetworkHelperAsync(subscription, progress, lifetime.Token));
        SetNetworkButtons(true);
        if (result?.IsSuccess == true)
        {
            SubscriptionUrlBox.Clear();
            NetworkHelperProgressBar.Value = 100;
            SelectNetworkNodeButton.IsEnabled = true;
            NetworkHelperStatusText.Text = "网络辅助已启用，正在读取可选节点……";
            await ShowNodeSelectionAsync(automatic: true);
        }
        else if (result is { ErrorMessage: not null })
        {
            NetworkHelperStatusText.Text = result.ErrorMessage;
            MessageBox.Show(this, result.ErrorMessage, "网络辅助未启用", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DisableNetworkHelperButton_Click(object sender, RoutedEventArgs e)
    {
        SetNetworkButtons(false);
        var result = await RunUiAsync(() => actions.DisableNetworkHelperAsync(lifetime.Token));
        SetNetworkButtons(true);
        NetworkHelperProgressBar.Value = 0;
        if (result?.IsSuccess == true)
        {
            SelectNetworkNodeButton.IsEnabled = false;
        }
        NetworkHelperStatusText.Text = result?.IsSuccess == true
            ? "已停止网络辅助，并恢复启用前的 Windows 当前用户代理设置。"
            : result?.ErrorMessage ?? "停用未完成，请重试。";
    }

    private void SetNetworkButtons(bool enabled)
    {
        EnableNetworkHelperButton.IsEnabled = enabled;
        DisableNetworkHelperButton.IsEnabled = enabled;
        if (!enabled)
        {
            SelectNetworkNodeButton.IsEnabled = false;
        }
    }

    private async void SelectNetworkNodeButton_Click(object sender, RoutedEventArgs e) =>
        await ShowNodeSelectionAsync(automatic: false);

    private async Task ShowNodeSelectionAsync(bool automatic)
    {
        SelectNetworkNodeButton.IsEnabled = false;
        var result = await RunUiAsync(() => actions.GetNetworkNodesAsync(lifetime.Token));
        SelectNetworkNodeButton.IsEnabled = true;
        if (result?.IsSuccess != true)
        {
            var message = result?.ErrorMessage ?? "暂时无法读取节点，请稍后点击“选择节点”重试。";
            NetworkHelperStatusText.Text = message;
            if (!automatic)
            {
                MessageBox.Show(this, message, "节点列表不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        var dialog = new NodeSelectionDialog(actions, result.Value!) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            NetworkHelperStatusText.Text = $"已应用节点：{dialog.SelectedNodeDisplayName}。网络辅助会在当前用户登录 Windows 后自动恢复运行。";
        }
        else
        {
            var current = result.Value!.FirstOrDefault(node => node.IsSelected)?.Name ?? "AUTO";
            NetworkHelperStatusText.Text = $"节点选择已关闭，继续使用：{NodeSelectionDialog.GetDisplayName(current)}。可随时点击“选择节点”更改。";
        }
    }

    private async void EnableAdministratorCompatibilityButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            this,
            "此操作会为内置 Administrator（SID 末尾 -500）启用管理员批准模式，并登记本助手在下次登录后自动打开。\n\nWindows 会在 15 秒后自动重启，请先保存所有工作。是否继续？",
            "启用兼容模式并重启",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await RunUiAsync(() =>
            viewModel.EnableBuiltInAdministratorCompatibilityAsync(lifetime.Token));
        if (result is { IsSuccess: false, ErrorMessage: not null })
        {
            MessageBox.Show(
                this,
                result.ErrorMessage,
                "未能启用兼容模式",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ReturnToInstallButton_Click(object sender, RoutedEventArgs e) =>
        viewModel.ReturnToInstallStep();

    private async void ShowGuideButton_Click(object sender, RoutedEventArgs e) =>
        await ShowGuideIfNeededAsync(force: true);

    private async void LaunchButton_Click(object sender, RoutedEventArgs e) =>
        await RunUiAsync(() => viewModel.LaunchAsync(lifetime.Token));

    private async void AdvertisementButton_Click(object sender, RoutedEventArgs e)
    {
        var target = await viewModel.TrackAdvertisementClickAsync(lifetime.Token);
        if (target is not null)
        {
            Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void DismissAdvertisementButton_Click(object sender, RoutedEventArgs e)
    {
        AdvertisementImage.Source = null;
        AdvertisementImage.Visibility = Visibility.Collapsed;
        viewModel.DismissAdvertisement();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void BrowseDownloadDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择官方安装文件保存目录",
            InitialDirectory = viewModel.DownloadDirectory,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            var result = viewModel.SelectDownloadDirectory(dialog.FolderName);
            if (!result.IsSuccess)
            {
                MessageBox.Show(this, result.ErrorMessage, "目录不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OpenMaintenanceButton_Click(object sender, RoutedEventArgs e) => OpenMaintenanceWindow();

    private void CleanupEverythingButton_Click(object sender, RoutedEventArgs e)
    {
        OpenMaintenanceWindow();
    }

    private void OpenMaintenanceWindow()
    {
        var maintenance = new MaintenanceWindow(actions) { Owner = this };
        maintenance.ShowDialog();
        viewModel.RefreshExternalState();
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        var url = sender is Button button
            ? button.Tag switch
            {
                Uri uri => uri.AbsoluteUri,
                string value => value,
                _ => null
            }
            : null;
        if (!string.IsNullOrWhiteSpace(url))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private async Task<OperationResult<T>?> RunUiAsync<T>(Func<Task<OperationResult<T>>> operation)
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

    private async Task LoadAdvertisementImageAsync()
    {
        var imageUri = viewModel.CurrentAdvertisement?.ImageUrl;
        if (imageUri is null || advertisementImageDownloader is null)
        {
            return;
        }

        var bytes = await advertisementImageDownloader.DownloadAsync(imageUri, lifetime.Token);
        if (bytes is null || !viewModel.IsAdvertisementVisible)
        {
            return;
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        AdvertisementImage.Source = bitmap;
        AdvertisementImage.Visibility = Visibility.Visible;
    }

    private async Task ShowGuideIfNeededAsync(bool force = false)
    {
        if (!viewModel.IsConfigureStep)
        {
            return;
        }
        try
        {
            await viewModel.LoadGuideAsync(lifetime.Token);
            if (force)
            {
                viewModel.ConsumeAutomaticGuideRequest();
            }
            else if (!viewModel.ConsumeAutomaticGuideRequest())
            {
                return;
            }

            var dialog = new GuideDialog(viewModel.CurrentGuide, advertisementImageDownloader) { Owner = this };
            dialog.ShowDialog();
            ApiKeyBox.Focus();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }
}
