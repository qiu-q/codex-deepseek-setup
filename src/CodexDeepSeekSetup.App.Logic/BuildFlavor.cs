namespace CodexDeepSeekSetup.App.Logic;

public enum BuildFlavor
{
    OpenSource,
    Internal
}

public sealed record BuildFlavorOptions(
    bool AllowAdjacentPayload,
    bool EnableProxyConfiguration,
    string? EmbeddedApiKey,
    Uri? AdvertisementEndpoint,
    Uri? GuideEndpoint)
{
    public static BuildFlavorOptions For(BuildFlavor flavor) =>
        new(
            flavor == BuildFlavor.Internal,
            true,
            null,
            flavor == BuildFlavor.Internal
                ? new Uri("https://www.qiuqiuqiu.top/xxx/codex-ad/")
                : null,
            flavor == BuildFlavor.Internal
                ? new Uri("https://www.qiuqiuqiu.top/xxx/codex-ad/")
                : null);
}
