namespace CodexDeepSeekSetup.Core.Downloads;

public static class OfficialPayloadUrls
{
    public const string CodexMsix = "https://persistent.oaistatic.com/codex-app-prod/ChatGPT-x64.msix";
    public const string OfflineLicense = "https://persistent.oaistatic.com/codex-app-prod/ChatGPT-License.xml";
}

public sealed record CodexPayload(string MsixPath, string LicensePath);

public sealed record DownloadProgress(string FileName, long BytesDownloaded, long? TotalBytes);
