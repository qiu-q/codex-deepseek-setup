namespace CodexDeepSeekSetup.App.Logic;

public enum SetupPhase
{
    None,
    Check,
    Download,
    Verify,
    Authorization,
    Deploy,
    Register,
    Cli,
    SaveState,
    Complete
}

public sealed record SetupProgress(
    string Message,
    double? Percent = null,
    SetupPhase Phase = SetupPhase.None,
    string? Detail = null,
    string? FileName = null,
    long? BytesCompleted = null,
    long? TotalBytes = null,
    bool IsIndeterminate = false);
