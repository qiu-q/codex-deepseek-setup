namespace CodexDeepSeekSetup.Core.Results;

public sealed record OperationResult<T>
{
    private OperationResult(bool isSuccess, T? value, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static OperationResult<T> Success(T value) => new(true, value, null, null);

    public static OperationResult<T> Failure(string errorCode, string errorMessage) =>
        new(false, default, errorCode, errorMessage);
}

public readonly record struct Unit;
