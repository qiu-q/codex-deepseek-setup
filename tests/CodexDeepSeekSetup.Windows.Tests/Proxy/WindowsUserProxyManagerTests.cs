using CodexDeepSeekSetup.Windows.Proxy;

namespace CodexDeepSeekSetup.Windows.Tests.Proxy;

public sealed class WindowsUserProxyManagerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codex-user-proxy-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CaptureAndApplyThenRestore_PreservesEveryPreviousValueExactly()
    {
        var registry = new MemoryProxyRegistry(new Dictionary<string, ProxyRegistryValue>
        {
            ["ProxyEnable"] = ProxyRegistryValue.Dword(0),
            ["ProxyServer"] = ProxyRegistryValue.String("old.proxy:8080"),
            ["ProxyOverride"] = ProxyRegistryValue.String("localhost;*.internal"),
            ["AutoConfigURL"] = ProxyRegistryValue.String("https://pac.example/config.pac")
        });
        var broadcaster = new RecordingBroadcaster();
        var manager = new WindowsUserProxyManager(registry, broadcaster, Path.Combine(root, "proxy-recovery.json"));

        var applied = await manager.CaptureAndApplyAsync(17890, default);

        Assert.True(applied.IsSuccess, applied.ErrorMessage);
        Assert.Equal(1, registry.Values["ProxyEnable"].DwordValue);
        Assert.Equal("127.0.0.1:17890", registry.Values["ProxyServer"].StringValue);
        Assert.Equal("<local>", registry.Values["ProxyOverride"].StringValue);
        Assert.False(registry.Values.ContainsKey("AutoConfigURL"));
        Assert.True(File.Exists(Path.Combine(root, "proxy-recovery.json")));

        var restored = await manager.RestoreAsync(default);

        Assert.True(restored.IsSuccess, restored.ErrorMessage);
        Assert.Equal(0, registry.Values["ProxyEnable"].DwordValue);
        Assert.Equal("old.proxy:8080", registry.Values["ProxyServer"].StringValue);
        Assert.Equal("localhost;*.internal", registry.Values["ProxyOverride"].StringValue);
        Assert.Equal("https://pac.example/config.pac", registry.Values["AutoConfigURL"].StringValue);
        Assert.False(File.Exists(Path.Combine(root, "proxy-recovery.json")));
        Assert.Equal(2, broadcaster.BroadcastCount);
    }

    [Fact]
    public async Task Restore_DeletesValuesThatDidNotExistBeforeActivation()
    {
        var registry = new MemoryProxyRegistry();
        var manager = new WindowsUserProxyManager(registry, new RecordingBroadcaster(), Path.Combine(root, "proxy-recovery.json"));
        Assert.True((await manager.CaptureAndApplyAsync(17890, default)).IsSuccess);

        var restored = await manager.RestoreAsync(default);

        Assert.True(restored.IsSuccess);
        Assert.Empty(registry.Values);
    }

    [Fact]
    public async Task RepairIfStale_RestoresPersistedSnapshotIdempotently()
    {
        var registry = new MemoryProxyRegistry(new Dictionary<string, ProxyRegistryValue>
        {
            ["ProxyEnable"] = ProxyRegistryValue.Dword(0)
        });
        var marker = Path.Combine(root, "proxy-recovery.json");
        var manager = new WindowsUserProxyManager(registry, new RecordingBroadcaster(), marker);
        Assert.True((await manager.CaptureAndApplyAsync(17890, default)).IsSuccess);

        Assert.True((await manager.RepairIfStaleAsync(default)).IsSuccess);
        Assert.True((await manager.RepairIfStaleAsync(default)).IsSuccess);
        Assert.Equal(0, registry.Values["ProxyEnable"].DwordValue);
        Assert.False(File.Exists(marker));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task CaptureAndApply_RejectsInvalidPortWithoutChangingRegistry(int port)
    {
        var registry = new MemoryProxyRegistry();
        var manager = new WindowsUserProxyManager(registry, new RecordingBroadcaster(), Path.Combine(root, "proxy-recovery.json"));

        var result = await manager.CaptureAndApplyAsync(port, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("proxy.settings.port", result.ErrorCode);
        Assert.Empty(registry.Values);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class MemoryProxyRegistry : IUserProxyRegistry
    {
        public MemoryProxyRegistry(Dictionary<string, ProxyRegistryValue>? values = null) =>
            Values = values ?? new Dictionary<string, ProxyRegistryValue>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ProxyRegistryValue> Values { get; }

        public ProxyRegistryValue Read(string name) =>
            Values.TryGetValue(name, out var value) ? value : ProxyRegistryValue.Missing;

        public void Write(string name, ProxyRegistryValue value)
        {
            if (!value.Exists)
            {
                Values.Remove(name);
            }
            else
            {
                Values[name] = value;
            }
        }
    }

    private sealed class RecordingBroadcaster : IProxySettingsBroadcaster
    {
        public int BroadcastCount { get; private set; }
        public void Broadcast() => BroadcastCount++;
    }
}
