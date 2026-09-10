using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Advertisements;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.App.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task InitializeAsync_WhenCodexIsMissing_ShowsOnlyInstallStep()
    {
        var viewModel = new MainWindowViewModel(new FakeActions());

        var result = await viewModel.InitializeAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(viewModel.IsInstallStep);
        Assert.False(viewModel.IsConfigureStep);
        Assert.False(viewModel.IsCompleteStep);
        Assert.Equal("准备安装 Codex", viewModel.StatusMessage);
        Assert.True(viewModel.CanDownload);
        Assert.False(viewModel.CanInstall);
    }

    [Fact]
    public async Task InitializeAsync_WhenCodexExists_AdvancesToConfigureStep()
    {
        var viewModel = new MainWindowViewModel(new FakeActions { IsCodexInstalled = true });

        var result = await viewModel.InitializeAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(viewModel.IsConfigureStep);
        Assert.Equal("Codex 已安装，下一步请准备 DeepSeek API Key", viewModel.StatusMessage);
        Assert.Contains("D:\\WindowsApps", viewModel.InstallationSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_WhenInstalledCodexHasBuiltInAdminWarning_StillAdvancesToConfigureStep()
    {
        var actions = new FakeActions
        {
            IsCodexInstalled = true,
            CheckFailure = OperationResult<Unit>.Failure(
                "windows.builtin_admin.restricted",
                "内置 Administrator 需要启用管理员批准模式并重启。")
        };
        var viewModel = new MainWindowViewModel(actions);

        var result = await viewModel.InitializeAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(viewModel.IsConfigureStep);
        Assert.True(viewModel.CanConfigure);
        Assert.Contains("可以继续配置 DeepSeek", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("管理员批准模式", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnableBuiltInAdministratorCompatibilityAsync_WhenWarningIsPresent_RequestsRestartSetup()
    {
        var actions = new FakeActions
        {
            IsCodexInstalled = true,
            CheckFailure = OperationResult<Unit>.Failure(
                "windows.builtin_admin.restricted",
                "内置 Administrator 需要启用管理员批准模式并重启。")
        };
        var viewModel = new MainWindowViewModel(actions);
        await viewModel.InitializeAsync(default);

        var result = await viewModel.EnableBuiltInAdministratorCompatibilityAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(actions.EnableBuiltInAdministratorCompatibilityWasCalled);
        Assert.True(viewModel.NeedsBuiltInAdministratorCompatibility);
        Assert.False(viewModel.CanEnableBuiltInAdministratorCompatibility);
        Assert.True(viewModel.RestartScheduled);
        Assert.Contains("重启", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectDownloadDirectory_UpdatesVisibleLocationAndAction()
    {
        var actions = new FakeActions();
        var viewModel = new MainWindowViewModel(actions);

        var result = viewModel.SelectDownloadDirectory(@"D:\CodexDownloads");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(@"D:\CodexDownloads", viewModel.DownloadDirectory);
        Assert.Equal(@"D:\CodexDownloads", actions.DownloadDirectory);
    }

    [Fact]
    public async Task SelectDownloadDirectory_AfterDownloadRequiresFreshDownload()
    {
        var viewModel = new MainWindowViewModel(new FakeActions());
        await viewModel.DownloadAsync(default);
        Assert.True(viewModel.CanInstall);

        var result = viewModel.SelectDownloadDirectory(@"D:\AnotherFolder");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(CodexInstallStage.NotDownloaded, viewModel.InstallStage);
        Assert.False(viewModel.CanInstall);
    }

    [Fact]
    public void SelectInstallDrive_RejectsDriveNotReportedByWindows()
    {
        var viewModel = new MainWindowViewModel(new FakeActions());

        var result = viewModel.SelectInstallDrive(@"Z:\");

        Assert.False(result.IsSuccess);
        Assert.Equal(@"D:\", viewModel.SelectedInstallDrive);
    }

    [Fact]
    public async Task DownloadAsync_OnSuccess_EnablesInstallWithoutAdvancingWizard()
    {
        var viewModel = new MainWindowViewModel(new FakeActions());
        await viewModel.InitializeAsync(default);

        var result = await viewModel.DownloadAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(viewModel.IsInstallStep);
        Assert.Equal(CodexInstallStage.ReadyToInstall, viewModel.InstallStage);
        Assert.True(viewModel.CanInstall);
        Assert.True(viewModel.CanDownload);
        Assert.False(viewModel.IsProgressVisible);
        Assert.Equal(0, viewModel.Progress);
    }

    [Fact]
    public async Task DownloadAsync_OnFailure_DoesNotEnableInstall()
    {
        var actions = new FakeActions { PrepareSucceeds = false };
        var viewModel = new MainWindowViewModel(actions);

        var result = await viewModel.DownloadAsync(default);

        Assert.False(result.IsSuccess);
        Assert.Equal(CodexInstallStage.NotDownloaded, viewModel.InstallStage);
        Assert.False(viewModel.CanInstall);
        Assert.True(viewModel.CanDownload);
    }

    [Fact]
    public async Task DownloadAsync_ProjectsDetailedFileProgressSpeedAndEta()
    {
        const long mib = 1024 * 1024;
        var actions = new FakeActions
        {
            PrepareProgress =
            [
                new SetupProgress("正在下载 ChatGPT-x64.msix", 20, SetupPhase.Download, "接收官方安装包", "ChatGPT-x64.msix", mib, 5 * mib),
                new SetupProgress("正在下载 ChatGPT-x64.msix", 70, SetupPhase.Download, "接收官方安装包", "ChatGPT-x64.msix", 3 * mib, 5 * mib)
            ]
        };
        var times = new Queue<DateTimeOffset>(
        [
            DateTimeOffset.Parse("2026-09-08T12:00:00+08:00"),
            DateTimeOffset.Parse("2026-09-08T12:00:01+08:00")
        ]);
        var viewModel = new MainWindowViewModel(actions, () => times.Dequeue());

        var result = await viewModel.DownloadAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("正在下载 ChatGPT-x64.msix", viewModel.ProgressTitle);
        Assert.Equal("接收官方安装包", viewModel.ProgressDetail);
        Assert.Equal("ChatGPT-x64.msix", viewModel.CurrentFileName);
        Assert.Equal(60, viewModel.FileProgress);
        Assert.Equal(70, viewModel.OverallProgress);
        Assert.Equal("3.00 MB / 5.00 MB · 2.00 MB/s · 约 1 秒", viewModel.TransferSummary);
        Assert.Single(viewModel.ProgressEntries);
        Assert.StartsWith("12:00:00", viewModel.ProgressEntries[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadAsync_TrimsDetailedHistoryToLatestTwoHundredEntries()
    {
        var updates = Enumerable.Range(0, 205)
            .Select(index => new SetupProgress($"阶段 {index}", index % 100, SetupPhase.Download, $"详细 {index}"))
            .ToArray();
        var actions = new FakeActions { PrepareProgress = updates };
        var viewModel = new MainWindowViewModel(actions);

        await viewModel.DownloadAsync(default);

        Assert.Equal(200, viewModel.ProgressEntries.Count);
        Assert.DoesNotContain("阶段 0", viewModel.ProgressEntries[0], StringComparison.Ordinal);
        Assert.Contains("阶段 204", viewModel.ProgressEntries[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallAsync_OnFailure_KeepsPreparedPayloadForDirectRetry()
    {
        var actions = new FakeActions { InstallFailuresRemaining = 1 };
        var viewModel = new MainWindowViewModel(actions);
        await viewModel.DownloadAsync(default);

        var first = await viewModel.InstallAsync(default);
        var second = await viewModel.InstallAsync(default);

        Assert.False(first.IsSuccess);
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal(1, actions.PrepareCallCount);
        Assert.Equal(2, actions.InstallCallCount);
        Assert.True(viewModel.IsConfigureStep);
    }

    [Fact]
    public async Task InstallAsync_WhenPreparedFilesDisappear_ReturnsToDownloadStage()
    {
        var actions = new FakeActions { InstallFailureCode = "payload.not.prepared" };
        var viewModel = new MainWindowViewModel(actions);
        await viewModel.DownloadAsync(default);

        var result = await viewModel.InstallAsync(default);

        Assert.False(result.IsSuccess);
        Assert.Equal(CodexInstallStage.NotDownloaded, viewModel.InstallStage);
        Assert.True(viewModel.CanDownload);
        Assert.False(viewModel.CanInstall);
        Assert.False(viewModel.CanUsePortable);
    }

    [Fact]
    public async Task InvalidKey_DoesNotAdvanceOrRetainSecret()
    {
        const string key = "sk-private12345678";
        var actions = new FakeActions { KeySucceeds = false };
        var viewModel = new MainWindowViewModel(actions);
        await viewModel.CheckAsync(default);
        await viewModel.DownloadAsync(default);
        await viewModel.InstallAsync(default);

        var result = await viewModel.ConfigureAsync(key, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(WizardStep.DeepSeek, viewModel.CurrentStep);
        Assert.DoesNotContain(key, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(key, string.Join('|', viewModel.GetType().GetProperties().Select(property => property.GetValue(viewModel)?.ToString())));
    }

    [Fact]
    public void Flavor_NeverEnablesProxyOrEmbeddedKey_AndOnlyInternalEnablesAdvertisements()
    {
        var openSource = BuildFlavorOptions.For(BuildFlavor.OpenSource);
        var internalBuild = BuildFlavorOptions.For(BuildFlavor.Internal);

        Assert.False(openSource.EnableProxyConfiguration);
        Assert.Null(openSource.EmbeddedApiKey);
        Assert.Null(openSource.AdvertisementEndpoint);
        Assert.False(internalBuild.EnableProxyConfiguration);
        Assert.Null(internalBuild.EmbeddedApiKey);
        Assert.Equal(
            "https://www.qiuqiuqiu.top/xxx/codex-ad/",
            internalBuild.AdvertisementEndpoint?.AbsoluteUri);
    }

    [Fact]
    public async Task LoadAdvertisementAsync_OnCompletedInternalFlow_ShowsAndTracksCampaign()
    {
        var advertisement = new FakeAdvertisementClient
        {
            Campaign = new AdCampaign(
                "campaign-1",
                "推广标题",
                "推广内容",
                "了解详情",
                new Uri("https://example.com/product"),
                null,
                null,
                null)
        };
        var viewModel = await CreateCompletedViewModelAsync(new FakeActions(), advertisement);

        await viewModel.LoadAdvertisementAsync(default);

        Assert.True(viewModel.IsAdvertisementVisible);
        Assert.Equal("推广标题", viewModel.CurrentAdvertisement?.Title);
        Assert.Equal([("campaign-1", AdEventType.Impression)], advertisement.Events);
    }

    [Fact]
    public async Task DismissAdvertisement_HidesCampaignWithoutOpeningBrowser()
    {
        var advertisement = new FakeAdvertisementClient
        {
            Campaign = new AdCampaign("campaign-1", "t", "b", "go", new Uri("https://example.com"), null, null, null)
        };
        var viewModel = await CreateCompletedViewModelAsync(new FakeActions(), advertisement);
        await viewModel.LoadAdvertisementAsync(default);

        viewModel.DismissAdvertisement();

        Assert.False(viewModel.IsAdvertisementVisible);
        Assert.Null(viewModel.GetAdvertisementTarget());
    }

    [Fact]
    public async Task TrackAdvertisementClickAsync_ReturnsTargetAndTracksDeliberateClick()
    {
        var advertisement = new FakeAdvertisementClient
        {
            Campaign = new AdCampaign("campaign-1", "t", "b", "go", new Uri("https://example.com/product"), null, null, null)
        };
        var viewModel = await CreateCompletedViewModelAsync(new FakeActions(), advertisement);
        await viewModel.LoadAdvertisementAsync(default);

        var target = await viewModel.TrackAdvertisementClickAsync(default);

        Assert.Equal("https://example.com/product", target?.AbsoluteUri);
        Assert.Contains(("campaign-1", AdEventType.Click), advertisement.Events);
    }

    [Fact]
    public async Task LaunchAsync_ReportsMissingWindowAsFailure()
    {
        var viewModel = new MainWindowViewModel(new FakeActions { LaunchSucceeds = false });
        await viewModel.CheckAsync(default);
        await viewModel.DownloadAsync(default);
        await viewModel.InstallAsync(default);
        await viewModel.ConfigureAsync("sk-valid12345678", default);

        var result = await viewModel.LaunchAsync(default);

        Assert.False(result.IsSuccess);
        Assert.Contains("窗口", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchAsync_IsBlockedUntilConfigurationSucceeds()
    {
        var actions = new FakeActions();
        var viewModel = new MainWindowViewModel(actions);

        var result = await viewModel.LaunchAsync(default);

        Assert.False(result.IsSuccess);
        Assert.False(actions.LaunchWasCalled);
    }

    [Fact]
    public async Task InstallPortableAsync_IsBlockedBeforePrimaryInstallFails()
    {
        var actions = new FakeActions();
        var viewModel = new MainWindowViewModel(actions);

        var result = await viewModel.InstallPortableAsync(default);

        Assert.False(result.IsSuccess);
        Assert.False(actions.PortableInstallWasCalled);
        Assert.Equal(WizardStep.Welcome, viewModel.CurrentStep);
    }

    [Fact]
    public async Task InstallPortableAsync_AdvancesAfterPrimaryInstallFails()
    {
        var actions = new FakeActions { InstallFailuresRemaining = 1 };
        var viewModel = new MainWindowViewModel(actions);
        await viewModel.DownloadAsync(default);
        var primaryResult = await viewModel.InstallAsync(default);

        var result = await viewModel.InstallPortableAsync(default);

        Assert.False(primaryResult.IsSuccess);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(actions.PortableInstallWasCalled);
        Assert.Equal(WizardStep.DeepSeek, viewModel.CurrentStep);
    }

    [Fact]
    public async Task CleanupAsync_IsBlockedUntilSetupIsComplete()
    {
        var actions = new FakeActions();
        var viewModel = new MainWindowViewModel(actions);

        var result = await viewModel.CleanupAsync(default);

        Assert.False(result.IsSuccess);
        Assert.False(actions.CleanupWasCalled);
        Assert.False(viewModel.ShouldExit);
    }

    [Fact]
    public async Task CleanupAsync_WhenCleanupFails_KeepsAssistantOpenForRetry()
    {
        var actions = new FakeActions { CleanupSucceeds = false };
        var viewModel = await CreateCompletedViewModelAsync(actions);

        var result = await viewModel.CleanupAsync(default);

        Assert.False(result.IsSuccess);
        Assert.True(actions.CleanupWasCalled);
        Assert.False(viewModel.ShouldExit);
        Assert.True(viewModel.CanCleanup);
        Assert.Contains("未能完全清理", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanupAsync_WhenCleanupSucceeds_RequestsApplicationExit()
    {
        var actions = new FakeActions { HasInstallLedger = true };
        var viewModel = await CreateCompletedViewModelAsync(actions);

        var result = await viewModel.CleanupAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(actions.CleanupWasCalled);
        Assert.True(viewModel.ShouldExit);
        Assert.False(viewModel.CanCleanup);
        Assert.False(viewModel.NeedsLegacyCleanupWarning);
    }

    private static async Task<MainWindowViewModel> CreateCompletedViewModelAsync(
        FakeActions actions,
        IAdvertisementClient? advertisementClient = null)
    {
        var viewModel = new MainWindowViewModel(actions, advertisementClient: advertisementClient);
        await viewModel.InitializeAsync(default);
        if (viewModel.IsInstallStep)
        {
            await viewModel.DownloadAsync(default);
            await viewModel.InstallAsync(default);
        }
        await viewModel.ConfigureAsync("sk-valid12345678", default);
        return viewModel;
    }

    private sealed class FakeAdvertisementClient : IAdvertisementClient
    {
        public AdCampaign? Campaign { get; init; }
        public List<(string CampaignId, AdEventType EventType)> Events { get; } = [];

        public Task<AdCampaign?> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Campaign);

        public Task TrackAsync(string campaignId, AdEventType eventType, CancellationToken cancellationToken)
        {
            Events.Add((campaignId, eventType));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeActions : IWizardActions
    {
        public bool IsCodexInstalled { get; set; }
        public bool HasInstallLedger { get; set; }
        public IReadOnlyList<InstallDriveChoice> InstallDriveChoices { get; } =
        [
            new(@"C:\", "系统 (C:) · 可用 20.0 GB", false),
            new(@"D:\", "数据 (D:) · 可用 50.0 GB", true)
        ];
        public string DownloadDirectory { get; private set; } = @"C:\Setup\Downloads";
        public string SelectedInstallDrive { get; private set; } = @"D:\";
        public string InstallationSummary => IsCodexInstalled
            ? @"已安装 OpenAI Codex 26.901.6511.0 · D:\WindowsApps"
            : "未检测到 Codex";
        public bool KeySucceeds { get; init; } = true;
        public bool PrepareSucceeds { get; init; } = true;
        public int InstallFailuresRemaining { get; set; }
        public string? InstallFailureCode { get; init; }
        public bool LaunchSucceeds { get; init; } = true;
        public bool CleanupSucceeds { get; init; } = true;
        public OperationResult<Unit>? CheckFailure { get; init; }
        public bool LaunchWasCalled { get; private set; }
        public bool PortableInstallWasCalled { get; private set; }
        public bool CleanupWasCalled { get; private set; }
        public bool EnableBuiltInAdministratorCompatibilityWasCalled { get; private set; }
        public int PrepareCallCount { get; private set; }
        public int InstallCallCount { get; private set; }
        public IReadOnlyList<SetupProgress>? PrepareProgress { get; init; }
        public OperationResult<Unit> SelectDownloadDirectory(string directory)
        {
            DownloadDirectory = directory;
            return OperationResult<Unit>.Success(default);
        }
        public OperationResult<Unit> SelectInstallDrive(string driveRoot)
        {
            if (InstallDriveChoices.All(item => item.RootPath != driveRoot))
            {
                return OperationResult<Unit>.Failure("drive.invalid", "磁盘不可用");
            }
            SelectedInstallDrive = driveRoot;
            return OperationResult<Unit>.Success(default);
        }
        public Task<OperationResult<Unit>> CheckAsync(CancellationToken cancellationToken) =>
            CheckFailure is null ? Ok() : Task.FromResult(CheckFailure);
        public Task<OperationResult<Unit>> PrepareCodexAsync(IProgress<SetupProgress>? progress, CancellationToken cancellationToken)
        {
            PrepareCallCount++;
            foreach (var update in PrepareProgress ?? [new SetupProgress("正在下载 OpenAI 官方文件", 60)])
            {
                progress?.Report(update);
            }
            if (!PrepareSucceeds)
            {
                return Task.FromResult(OperationResult<Unit>.Failure("download.network.failed", "官方文件下载失败"));
            }
            return Ok();
        }
        public Task<OperationResult<Unit>> InstallPreparedCodexAsync(IProgress<SetupProgress>? progress, CancellationToken cancellationToken)
        {
            InstallCallCount++;
            if (InstallFailureCode is not null)
            {
                return Task.FromResult(OperationResult<Unit>.Failure(InstallFailureCode, "已准备文件不存在"));
            }
            if (InstallFailuresRemaining > 0)
            {
                InstallFailuresRemaining--;
                return Task.FromResult(OperationResult<Unit>.Failure("install.appx.failed", "官方离线部署失败"));
            }
            IsCodexInstalled = true;
            return Ok();
        }
        public Task<OperationResult<Unit>> ValidateAndConfigureAsync(string apiKey, CancellationToken cancellationToken) =>
            KeySucceeds ? Ok() : Task.FromResult(OperationResult<Unit>.Failure("key.invalid", "API Key 无效"));
        public Task<OperationResult<Unit>> InstallPreparedPortableAsync(IProgress<SetupProgress>? progress, CancellationToken cancellationToken)
        {
            PortableInstallWasCalled = true;
            IsCodexInstalled = true;
            return Ok();
        }
        public Task<OperationResult<Unit>> LaunchAsync(CancellationToken cancellationToken)
        {
            LaunchWasCalled = true;
            return LaunchSucceeds ? Ok() : Task.FromResult(OperationResult<Unit>.Failure("launch.window.missing", "没有检测到 Codex 窗口"));
        }
        public Task<OperationResult<Unit>> CleanupAsync(CancellationToken cancellationToken)
        {
            CleanupWasCalled = true;
            return CleanupSucceeds
                ? Ok()
                : Task.FromResult(OperationResult<Unit>.Failure("cleanup.failed", "未能完全清理，请重试"));
        }
        public Task<OperationResult<Unit>> EnableBuiltInAdministratorCompatibilityAsync(CancellationToken cancellationToken)
        {
            EnableBuiltInAdministratorCompatibilityWasCalled = true;
            return Ok();
        }
        private static Task<OperationResult<Unit>> Ok() => Task.FromResult(OperationResult<Unit>.Success(default));
    }
}
