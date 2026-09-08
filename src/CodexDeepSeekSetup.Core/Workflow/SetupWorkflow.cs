using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Core.Workflow;

public interface ISetupActions
{
    Task<OperationResult<Unit>> CheckReadinessAsync(CancellationToken cancellationToken);
    Task<OperationResult<Unit>> DownloadAsync(CancellationToken cancellationToken);
    Task<OperationResult<Unit>> InstallAsync(CancellationToken cancellationToken);
    Task<OperationResult<Unit>> ValidateAndStoreKeyAsync(string apiKey, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> ConfigureAsync(CancellationToken cancellationToken);
    Task<OperationResult<Unit>> VerifyAsync(CancellationToken cancellationToken);
}

public sealed class SetupWorkflow(SetupStateStore store, ISetupActions actions)
{
    public SetupState State { get; private set; } = new();

    public async Task InitializeAsync(CancellationToken cancellationToken) =>
        State = await store.LoadAsync(cancellationToken).ConfigureAwait(false);

    public Task<OperationResult<Unit>> RunReadinessAsync(CancellationToken cancellationToken) =>
        RunAsync(SetupStage.NotStarted, SetupStage.Ready, actions.CheckReadinessAsync, cancellationToken, allowLaterStage: true);

    public Task<OperationResult<Unit>> DownloadAsync(CancellationToken cancellationToken) =>
        RunAsync(SetupStage.Ready, SetupStage.Downloaded, actions.DownloadAsync, cancellationToken);

    public Task<OperationResult<Unit>> InstallAsync(CancellationToken cancellationToken) =>
        RunAsync(SetupStage.Downloaded, SetupStage.CodexInstalled, actions.InstallAsync, cancellationToken);

    public Task<OperationResult<Unit>> ValidateKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return RunAsync(
            SetupStage.CodexInstalled,
            SetupStage.KeyValidated,
            token => actions.ValidateAndStoreKeyAsync(apiKey, token),
            cancellationToken);
    }

    public Task<OperationResult<Unit>> ConfigureAsync(CancellationToken cancellationToken) =>
        RunAsync(SetupStage.KeyValidated, SetupStage.Configured, actions.ConfigureAsync, cancellationToken);

    public Task<OperationResult<Unit>> VerifyAsync(CancellationToken cancellationToken) =>
        RunAsync(SetupStage.Configured, SetupStage.Verified, actions.VerifyAsync, cancellationToken);

    private async Task<OperationResult<Unit>> RunAsync(
        SetupStage required,
        SetupStage next,
        Func<CancellationToken, Task<OperationResult<Unit>>> action,
        CancellationToken cancellationToken,
        bool allowLaterStage = false)
    {
        if (allowLaterStage && State.Stage > required)
        {
            return OperationResult<Unit>.Success(default);
        }

        if (State.Stage != required)
        {
            return OperationResult<Unit>.Failure("workflow.order", "请按顺序完成安装步骤。");
        }

        var result = await action(cancellationToken).ConfigureAwait(false);
        State = result.IsSuccess
            ? State with { Stage = next, LastErrorCode = null }
            : State with { LastErrorCode = result.ErrorCode };
        await store.SaveAsync(State, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
