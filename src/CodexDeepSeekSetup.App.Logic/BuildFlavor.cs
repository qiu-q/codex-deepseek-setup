namespace CodexDeepSeekSetup.App.Logic;

public enum BuildFlavor
{
    OpenSource,
    Internal
}

public sealed record BuildFlavorOptions(
    bool AllowAdjacentPayload,
    bool EnableProxyConfiguration,
    string? EmbeddedApiKey)
{
    public static BuildFlavorOptions For(BuildFlavor flavor) =>
        new(flavor == BuildFlavor.Internal, false, null);
}
