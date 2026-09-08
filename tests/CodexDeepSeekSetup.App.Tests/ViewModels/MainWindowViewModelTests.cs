using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.App.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
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

    private sealed class FakeActions : IWizardActions
    {
        public bool IsCodexInstalled { get; private set; }
        public bool KeySucceeds { get; init; } = true;
        public bool InstallSucceeds { get; init; } = true;
        public bool LaunchSucceeds { get; init; } = true;
        public bool LaunchWasCalled { get; private set; }
        public bool PortableInstallWasCalled { get; private set; }
        public Task<OperationResult<Unit>> CheckAsync(CancellationToken cancellationToken) => Ok();
        public Task<OperationResult<Unit>> InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (!InstallSucceeds)
            {
                return Task.FromResult(OperationResult<Unit>.Failure("install.appx.failed", "官方离线部署失败"));
            }
            IsCodexInstalled = true;
            return Ok();
        }
        public Task<OperationResult<Unit>> ValidateAndConfigureAsync(string apiKey, CancellationToken cancellationToken) =>
            KeySucceeds ? Ok() : Task.FromResult(OperationResult<Unit>.Failure("key.invalid", "API Key 无效"));
        public Task<OperationResult<Unit>> InstallPortableAsync(IProgress<double>? progress, CancellationToken cancellationToken)
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
        private static Task<OperationResult<Unit>> Ok() => Task.FromResult(OperationResult<Unit>.Success(default));
    }
}
