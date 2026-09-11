using CodexDeepSeekSetup.Core.Security;

namespace CodexDeepSeekSetup.Core.Results;

public sealed record OperationResult<T>
{
    private OperationResult(
        bool isSuccess,
        T? value,
        string? errorCode,
        string? errorMessage,
        string? diagnosticDetails)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage is null ? null : SecretRedactor.Redact(errorMessage);
        DiagnosticDetails = diagnosticDetails is null ? null : SecretRedactor.Redact(diagnosticDetails);
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public string? DiagnosticDetails { get; }

    public static OperationResult<T> Success(T value) => new(true, value, null, null, null);

    public static OperationResult<T> Failure(
        string errorCode,
        string errorMessage,
        string? diagnosticDetails = null) =>
        new(false, default, errorCode, errorMessage, diagnosticDetails);
}

public readonly record struct Unit;
