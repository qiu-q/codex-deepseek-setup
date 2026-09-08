namespace CodexDeepSeekSetup.Core.Configuration;

public sealed record CodexConfigRequest(
    string CodexHome,
    string Model,
    string CredentialHelperPath,
    string CredentialTarget);

public sealed record ConfigApplyResult(
    string BackupDirectory,
    string ConfigPath,
    string ModelCatalogPath);
