using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Core.Downloads;

public sealed class OfficialOriginPolicy
{
    private static readonly HashSet<string> AllowedUrls = new(StringComparer.Ordinal)
    {
        OfficialPayloadUrls.CodexMsix,
        OfficialPayloadUrls.OfflineLicense
    };

    public OperationResult<Uri> EnsureAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme != Uri.UriSchemeHttps || !AllowedUrls.Contains(uri.AbsoluteUri))
        {
            return OperationResult<Uri>.Failure(
                "download.origin.rejected",
                "下载地址不是允许的官方地址");
        }

        return OperationResult<Uri>.Success(uri);
    }
}
