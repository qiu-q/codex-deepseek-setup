using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Core.Workflow;

namespace CodexDeepSeekSetup.Core.Tests.Workflow;

public sealed class SetupWorkflowTests : IDisposable
{
    private readonly string statePath = Path.Combine(Path.GetTempPath(), $"codex-state-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task ConfigureAsync_DoesNotWriteConfigWhenPackageInstallFailed()
    {
        var actions = new FakeActions { InstallSucceeds = false };
        var workflow = new SetupWorkflow(new SetupStateStore(statePath), actions);

        await workflow.RunReadinessAsync(default);
        await workflow.DownloadAsync(default);
        var install = await workflow.InstallAsync(default);
        var configure = await workflow.ConfigureAsync(default);

        Assert.False(install.IsSuccess);
        Assert.False(configure.IsSuccess);
        Assert.False(actions.ConfigWasWritten);
    }

    [Fact]
    public async Task ValidateKeyAsync_DoesNotPersistTheApiKey()
    {
        const string apiKey = "sk-this-must-never-be-in-state";
        var workflow = new SetupWorkflow(new SetupStateStore(statePath), new FakeActions());
        await workflow.RunReadinessAsync(default);
        await workflow.DownloadAsync(default);
        await workflow.InstallAsync(default);

        var result = await workflow.ValidateKeyAsync(apiKey, default);
        var stateText = await File.ReadAllTextAsync(statePath);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(apiKey, stateText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", stateText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulRun_AdvancesThroughVerifiedStage()
    {
        var workflow = new SetupWorkflow(new SetupStateStore(statePath), new FakeActions());

        Assert.True((await workflow.RunReadinessAsync(default)).IsSuccess);
        Assert.True((await workflow.DownloadAsync(default)).IsSuccess);
        Assert.True((await workflow.InstallAsync(default)).IsSuccess);
        Assert.True((await workflow.ValidateKeyAsync("sk-valid12345678", default)).IsSuccess);
        Assert.True((await workflow.ConfigureAsync(default)).IsSuccess);
        Assert.True((await workflow.VerifyAsync(default)).IsSuccess);
        Assert.Equal(SetupStage.Verified, workflow.State.Stage);
    }

    public void Dispose()
    {
        File.Delete(statePath);
        GC.SuppressFinalize(this);
    }

    private sealed class FakeActions : ISetupActions
    {
        public bool InstallSucceeds { get; init; } = true;
        public bool ConfigWasWritten { get; private set; }

        public Task<OperationResult<Unit>> CheckReadinessAsync(CancellationToken cancellationToken) => Ok();
        public Task<OperationResult<Unit>> DownloadAsync(CancellationToken cancellationToken) => Ok();
        public Task<OperationResult<Unit>> InstallAsync(CancellationToken cancellationToken) =>
            InstallSucceeds ? Ok() : Failed("install.failed");
        public Task<OperationResult<Unit>> ValidateAndStoreKeyAsync(string apiKey, CancellationToken cancellationToken) => Ok();
        public Task<OperationResult<Unit>> ConfigureAsync(CancellationToken cancellationToken)
        {
            ConfigWasWritten = true;
            return Ok();
        }
        public Task<OperationResult<Unit>> VerifyAsync(CancellationToken cancellationToken) => Ok();

        private static Task<OperationResult<Unit>> Ok() => Task.FromResult(OperationResult<Unit>.Success(default));
        private static Task<OperationResult<Unit>> Failed(string code) =>
            Task.FromResult(OperationResult<Unit>.Failure(code, "failed"));
    }
}
