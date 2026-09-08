namespace CodexDeepSeekSetup.Core.Workflow;

public enum SetupStage
{
    NotStarted,
    Ready,
    Downloaded,
    CodexInstalled,
    KeyValidated,
    Configured,
    Verified
}

public sealed record SetupState(
    SetupStage Stage = SetupStage.NotStarted,
    string? MsixPath = null,
    string? LicensePath = null,
    string? LastErrorCode = null);
