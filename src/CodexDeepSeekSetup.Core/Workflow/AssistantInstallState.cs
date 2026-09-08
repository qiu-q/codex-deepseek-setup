namespace CodexDeepSeekSetup.Core.Workflow;

public sealed record AssistantInstallState(
    int SchemaVersion,
    bool CodexExistedBefore,
    string InstallMode,
    string? PreviousCodexCliPath,
    bool DeepSeekConfigured,
    string DownloadCache,
    string? PortableDirectory,
    string? CliDirectory,
    string CredentialHelperPath,
    string AssistantDirectory);
