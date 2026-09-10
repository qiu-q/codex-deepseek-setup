using CodexDeepSeekSetup.Windows.Proxy;

namespace CodexDeepSeekSetup.Windows.Tests.Proxy;

public sealed class ProxyProcessSupervisorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codex-proxy-process-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartAsync_RefusesOccupiedPortWithoutStartingProcess()
    {
        var platform = new FakeProcessPlatform { PortInitiallyOpen = true };
        var supervisor = new ProxyProcessSupervisor(platform, Path.Combine(root, "process.json"));

        var result = await supervisor.StartAsync(CreateExecutable(), root, 17890, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("proxy.port.occupied", result.ErrorCode);
        Assert.Equal(0, platform.StartCount);
    }

    [Fact]
    public async Task StartAsync_WritesOwnedStateOnlyAfterLoopbackPortIsReady()
    {
        var executable = CreateExecutable();
        var platform = new FakeProcessPlatform { PortReadyAfterStart = true, StartedProcessId = 321 };
        var statePath = Path.Combine(root, "process.json");
        var supervisor = new ProxyProcessSupervisor(platform, statePath);

        var result = await supervisor.StartAsync(executable, root, 17890, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(321, result.Value);
        Assert.True(File.Exists(statePath));
        Assert.Equal(executable, platform.LastExecutable);
        Assert.Equal(["-d", Path.GetFullPath(root)], platform.LastArguments);
    }

    [Fact]
    public async Task StopAsync_DoesNotTerminatePidWhoseExecutableDoesNotMatchState()
    {
        var executable = CreateExecutable();
        var platform = new FakeProcessPlatform
        {
            PortReadyAfterStart = true,
            StartedProcessId = 321,
            RunningExecutableOverride = Path.Combine(root, "unrelated.exe")
        };
        var statePath = Path.Combine(root, "process.json");
        var supervisor = new ProxyProcessSupervisor(platform, statePath);
        Assert.True((await supervisor.StartAsync(executable, root, 17890, default)).IsSuccess);

        var result = await supervisor.StopAsync(default);

        Assert.False(result.IsSuccess);
        Assert.Equal("proxy.process.identity", result.ErrorCode);
        Assert.Empty(platform.TerminatedProcessIds);
        Assert.True(File.Exists(statePath));
    }

    [Fact]
    public async Task StopAsync_TerminatesMatchingProcessAndDeletesState()
    {
        var executable = CreateExecutable();
        var platform = new FakeProcessPlatform { PortReadyAfterStart = true, StartedProcessId = 321 };
        var statePath = Path.Combine(root, "process.json");
        var supervisor = new ProxyProcessSupervisor(platform, statePath);
        Assert.True((await supervisor.StartAsync(executable, root, 17890, default)).IsSuccess);

        var result = await supervisor.StopAsync(default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal([321], platform.TerminatedProcessIds);
        Assert.False(File.Exists(statePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string CreateExecutable()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "mihomo.exe");
        File.WriteAllText(path, "test");
        return Path.GetFullPath(path);
    }

    private sealed class FakeProcessPlatform : IProxyProcessPlatform
    {
        public bool PortInitiallyOpen { get; init; }
        public bool PortReadyAfterStart { get; init; }
        public int StartedProcessId { get; init; } = 100;
        public string? RunningExecutableOverride { get; init; }
        public int StartCount { get; private set; }
        public string? LastExecutable { get; private set; }
        public IReadOnlyList<string>? LastArguments { get; private set; }
        public List<int> TerminatedProcessIds { get; } = [];
        private string? startedExecutable;

        public Task<bool> IsLoopbackPortOpenAsync(int port, CancellationToken cancellationToken) =>
            Task.FromResult(StartCount == 0 ? PortInitiallyOpen : PortReadyAfterStart);

        public int Start(string executable, IReadOnlyList<string> arguments, string workingDirectory)
        {
            StartCount++;
            LastExecutable = executable;
            LastArguments = arguments.ToArray();
            startedExecutable = executable;
            return StartedProcessId;
        }

        public bool IsRunning(int processId) => processId == StartedProcessId;

        public string? GetExecutablePath(int processId) => RunningExecutableOverride ?? startedExecutable;

        public void Terminate(int processId) => TerminatedProcessIds.Add(processId);
    }
}
