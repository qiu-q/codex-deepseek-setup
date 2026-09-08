using CodexDeepSeekSetup.App.Logic;
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
    }

    [Fact]
    public async Task InitializeAsync_WhenCodexExists_AdvancesToConfigureStep()
    {
        var viewModel = new MainWindowViewModel(new FakeActions { IsCodexInstalled = true });

        var result = await viewModel.InitializeAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(viewModel.IsConfigureStep);
        Assert.Equal("Codex 已安装，下一步请准备 DeepSeek API Key", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallAsync_OnSuccess_AdvancesAndClearsProgress()
    {
        var viewModel = new MainWindowViewModel(new FakeActions());
        await viewModel.InitializeAsync(default);

        var result = await viewModel.InstallAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(viewModel.IsConfigureStep);
        Assert.False(viewModel.IsProgressVisible);
        Assert.Equal(0, viewModel.Progress);
    }

    [Fact]
    public async Task InvalidKey_DoesNotAdvanceOrRetainSecret()
    {
        const string key = "sk-private12345678";
        var actions = new FakeActions { KeySucceeds = false };
        var viewModel = new MainWindowViewModel(actions);
        await viewModel.CheckAsync(default);
        await viewModel.InstallAsync(default);

        var result = await viewModel.ConfigureAsync(key, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(WizardStep.DeepSeek, viewModel.CurrentStep);
        Assert.DoesNotContain(key, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(key, string.Join('|', viewModel.GetType().GetProperties().Select(property => property.GetValue(viewModel)?.ToString())));
    }

    [Fact]
    public void Flavor_NeverEnablesProxyOrEmbeddedKey()
    {
        foreach (var flavor in Enum.GetValues<BuildFlavor>())
        {
            var options = BuildFlavorOptions.For(flavor);
            Assert.False(options.EnableProxyConfiguration);
            Assert.Null(options.EmbeddedApiKey);
        }
    }

    [Fact]
    public async Task LaunchAsync_ReportsMissingWindowAsFailure()
    {
        var viewModel = new MainWindowViewModel(new FakeActions { LaunchSucceeds = false });
        await viewModel.CheckAsync(default);
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
        var actions = new FakeActions { InstallSucceeds = false };
        var viewModel = new MainWindowViewModel(actions);
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

    private static async Task<MainWindowViewModel> CreateCompletedViewModelAsync(FakeActions actions)
    {
        var viewModel = new MainWindowViewModel(actions);
        await viewModel.InitializeAsync(default);
        if (viewModel.IsInstallStep)
        {
            await viewModel.InstallAsync(default);
        }
        await viewModel.ConfigureAsync("sk-valid12345678", default);
        return viewModel;
    }

    private sealed class FakeActions : IWizardActions
    {
        public bool IsCodexInstalled { get; set; }
        public bool HasInstallLedger { get; set; }
        public bool KeySucceeds { get; init; } = true;
        public bool InstallSucceeds { get; init; } = true;
        public bool LaunchSucceeds { get; init; } = true;
        public bool CleanupSucceeds { get; init; } = true;
        public bool LaunchWasCalled { get; private set; }
        public bool PortableInstallWasCalled { get; private set; }
        public bool CleanupWasCalled { get; private set; }
        public Task<OperationResult<Unit>> CheckAsync(CancellationToken cancellationToken) => Ok();
        public Task<OperationResult<Unit>> InstallAsync(IProgress<SetupProgress>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(new SetupProgress("正在下载 OpenAI 官方文件", 60));
            if (!InstallSucceeds)
            {
                return Task.FromResult(OperationResult<Unit>.Failure("install.appx.failed", "官方离线部署失败"));
            }
            IsCodexInstalled = true;
            return Ok();
        }
        public Task<OperationResult<Unit>> ValidateAndConfigureAsync(string apiKey, CancellationToken cancellationToken) =>
            KeySucceeds ? Ok() : Task.FromResult(OperationResult<Unit>.Failure("key.invalid", "API Key 无效"));
        public Task<OperationResult<Unit>> InstallPortableAsync(IProgress<SetupProgress>? progress, CancellationToken cancellationToken)
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
        private static Task<OperationResult<Unit>> Ok() => Task.FromResult(OperationResult<Unit>.Success(default));
    }
}
