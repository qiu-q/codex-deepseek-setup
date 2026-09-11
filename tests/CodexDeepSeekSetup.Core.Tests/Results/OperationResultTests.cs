using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Core.Tests.Results;

public sealed class OperationResultTests
{
    [Fact]
    public void Success_PreservesNullErrorFields()
    {
        var result = OperationResult<Unit>.Success(default);

        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.DiagnosticDetails);
    }
}
