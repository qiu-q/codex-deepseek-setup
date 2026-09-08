using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Windows.Security;

public interface ISecretStore
{
    OperationResult<Unit> Write(string target, string secret);

    OperationResult<string> Read(string target);

    OperationResult<Unit> Delete(string target);
}
