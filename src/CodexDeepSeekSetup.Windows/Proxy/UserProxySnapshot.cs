namespace CodexDeepSeekSetup.Windows.Proxy;

public readonly record struct ProxyRegistryValue(bool Exists, int? DwordValue, string? StringValue)
{
    public static ProxyRegistryValue Missing => new(false, null, null);
    public static ProxyRegistryValue Dword(int value) => new(true, value, null);
    public static ProxyRegistryValue String(string? value) => new(true, null, value ?? string.Empty);
}

public sealed record UserProxySnapshot(
    ProxyRegistryValue ProxyEnable,
    ProxyRegistryValue ProxyServer,
    ProxyRegistryValue ProxyOverride,
    ProxyRegistryValue AutoConfigUrl);
