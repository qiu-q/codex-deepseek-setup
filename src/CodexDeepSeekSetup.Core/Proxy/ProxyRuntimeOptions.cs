namespace CodexDeepSeekSetup.Core.Proxy;

public sealed record ProxyRuntimeOptions(
    string Version,
    string RuntimeDirectory,
    Uri PrimaryArchiveUri,
    Uri FallbackArchiveUri,
    string ExpectedSha256,
    string ArchiveFileName,
    string ArchiveEntryName)
{
    public const string PinnedVersion = "v1.19.30";
    public const string PinnedArchiveSha256 = "289fde5e29d37a5b3326480590d8b3551c5bf7f8737290355c19bce74d57a563";

    public static ProxyRuntimeOptions CreateDefault(string runtimeDirectory) => new(
        PinnedVersion,
        runtimeDirectory,
        new Uri("https://www.qiuqiuqiu.top/xxx/codex-network/v1.19.30/mihomo-windows-amd64-compatible-v1.19.30.zip"),
        new Uri("https://github.com/MetaCubeX/mihomo/releases/download/v1.19.30/mihomo-windows-amd64-compatible-v1.19.30.zip"),
        PinnedArchiveSha256,
        "mihomo-windows-amd64-compatible-v1.19.30.zip",
        "mihomo-windows-amd64-compatible.exe");
}

public sealed record ProxyDownloadProgress(long BytesDownloaded, long? TotalBytes);
