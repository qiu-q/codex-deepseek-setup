using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.App.Tests.ViewModels;

public sealed class MaintenanceViewModelTests
{
    [Fact]
    public async Task RefreshAsync_UsesSafeDefaultSelectionsAndTotalsTheirSize()
    {
        var actions = new FakeMaintenanceActions
        {
            ScanItems =
            [
                new("downloads", "安装文件", @"D:\Downloads", 12, true, false, false),
                new("codex-home", "会话数据", @"C:\Users\test\.codex", 30, false, false, true)
            ]
        };
        var viewModel = new MaintenanceViewModel(actions);

        var result = await viewModel.RefreshAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, viewModel.Items.Count);
        Assert.True(viewModel.Items.Single(item => item.Id == "downloads").IsSelected);
        Assert.False(viewModel.Items.Single(item => item.Id == "codex-home").IsSelected);
        Assert.Equal(12, viewModel.SelectedSizeBytes);
    }

    [Fact]
    public async Task CleanupSelectedAsync_SendsOnlyCheckedIdsAndPropagatesSelfDelete()
    {
        var actions = new FakeMaintenanceActions
        {
            ScanItems =
            [
                new("downloads", "安装文件", @"D:\Downloads", 12, true, false, false),
                new("assistant-self", "安装助手", @"D:\Setup", null, false, false, true)
            ],
            CleanupOutcome = new MaintenanceCleanupOutcome(true)
        };
        var viewModel = new MaintenanceViewModel(actions);
        await viewModel.RefreshAsync(default);
        viewModel.Items.Single(item => item.Id == "downloads").IsSelected = false;
        viewModel.Items.Single(item => item.Id == "assistant-self").IsSelected = true;

        var result = await viewModel.CleanupSelectedAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(["assistant-self"], actions.LastCleanupIds);
        Assert.True(viewModel.ShouldExitApplication);
    }

    [Fact]
    public async Task CleanupSelectedAsync_OnPartialFailureRescansToAvoidStaleSelections()
    {
        var actions = new FakeMaintenanceActions
        {
            ScanItems = [new("downloads", "安装文件", @"D:\Downloads", 12, true, false, false)],
            CleanupFailure = true
        };
        var viewModel = new MaintenanceViewModel(actions);
        await viewModel.RefreshAsync(default);

        var result = await viewModel.CleanupSelectedAsync(default);

        Assert.False(result.IsSuccess);
        Assert.Equal(2, actions.ScanCallCount);
    }

    private sealed class FakeMaintenanceActions : IMaintenanceActions
    {
        public IReadOnlyList<MaintenanceArtifactInfo> ScanItems { get; init; } = [];
        public MaintenanceCleanupOutcome CleanupOutcome { get; init; } = new(false);
        public bool CleanupFailure { get; init; }
        public int ScanCallCount { get; private set; }
        public IReadOnlyList<string> LastCleanupIds { get; private set; } = [];
        public IReadOnlyList<InstallDriveChoice> InstallDriveChoices { get; } =
            [new(@"D:\", "数据 (D:)", true)];
        public string SelectedInstallDrive => @"D:\";

        public Task<OperationResult<IReadOnlyList<MaintenanceArtifactInfo>>> ScanAsync(CancellationToken cancellationToken)
        {
            ScanCallCount++;
            return Task.FromResult(OperationResult<IReadOnlyList<MaintenanceArtifactInfo>>.Success(ScanItems));
        }

        public Task<OperationResult<Unit>> MoveInstalledCodexAsync(string driveRoot, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<Unit>.Success(default));

        public Task<OperationResult<MaintenanceCleanupOutcome>> CleanupSelectedAsync(
            IReadOnlyCollection<string> artifactIds,
            CancellationToken cancellationToken)
        {
            LastCleanupIds = artifactIds.ToArray();
            if (CleanupFailure)
            {
                return Task.FromResult(OperationResult<MaintenanceCleanupOutcome>.Failure(
                    "cleanup.partial",
                    "部分内容未完成"));
            }
            return Task.FromResult(OperationResult<MaintenanceCleanupOutcome>.Success(CleanupOutcome));
        }
    }
}
