using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.App;

public interface INetworkNodeActions
{
    Task<OperationResult<int>> GetNetworkNodeDelayAsync(string nodeName, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> SelectNetworkNodeAsync(string nodeName, CancellationToken cancellationToken);
}
