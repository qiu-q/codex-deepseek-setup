using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Windows.Security;

public interface ISecretStore
{
    OperationResult<Unit> Write(string target, string secret);

    OperationResult<string> Read(string target);

    OperationResult<bool> Exists(string target)
    {
        var result = Read(target);
        return result.IsSuccess
            ? OperationResult<bool>.Success(true)
            : result.ErrorCode == "credential.not_found"
                ? OperationResult<bool>.Success(false)
                : OperationResult<bool>.Failure(result.ErrorCode!, result.ErrorMessage!);
    }

    OperationResult<Unit> Delete(string target);
}
