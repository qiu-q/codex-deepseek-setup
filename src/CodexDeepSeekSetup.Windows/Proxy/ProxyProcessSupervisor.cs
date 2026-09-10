using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Windows.Proxy;

public interface IProxyProcessPlatform
{
    Task<bool> IsLoopbackPortOpenAsync(int port, CancellationToken cancellationToken);
    int Start(string executable, IReadOnlyList<string> arguments, string workingDirectory);
    bool IsRunning(int processId);
    string? GetExecutablePath(int processId);
    void Terminate(int processId);
}

public sealed class ProxyProcessSupervisor(IProxyProcessPlatform platform, string statePath)
{
    public async Task<OperationResult<int>> StartAsync(
        string executable,
        string dataDirectory,
        int port,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(executable) || !Directory.Exists(dataDirectory) || port is < 1 or > 65535)
        {
            return OperationResult<int>.Failure("proxy.process.arguments", "Mihomo 启动参数无效");
        }

        if (await platform.IsLoopbackPortOpenAsync(port, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<int>.Failure("proxy.port.occupied", $"本地端口 {port} 已被其他程序占用");
        }

        var fullExecutable = Path.GetFullPath(executable);
        var fullDataDirectory = Path.GetFullPath(dataDirectory);
        int processId;
        try
        {
            processId = platform.Start(fullExecutable, ["-d", fullDataDirectory], fullDataDirectory);
        }
        catch
        {
            return OperationResult<int>.Failure("proxy.process.start", "无法启动 Mihomo");
        }

        var ready = false;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!platform.IsRunning(processId))
            {
                break;
            }

            if (await platform.IsLoopbackPortOpenAsync(port, cancellationToken).ConfigureAwait(false))
            {
                ready = true;
                break;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        if (!ready)
        {
            TerminateIfOwned(processId, fullExecutable);
            return OperationResult<int>.Failure("proxy.process.readiness", "Mihomo 未能在本地端口启动");
        }

        try
        {
            var fullStatePath = Path.GetFullPath(statePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullStatePath)!);
            var partial = fullStatePath + ".partial";
            File.WriteAllText(partial, JsonSerializer.Serialize(
                new ProxyProcessState(processId, fullExecutable, port),
                ProxyJsonContext.Default.ProxyProcessState));
            File.Move(partial, fullStatePath, overwrite: true);
            return OperationResult<int>.Success(processId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            TerminateIfOwned(processId, fullExecutable);
            return OperationResult<int>.Failure("proxy.process.state", "无法保存 Mihomo 进程状态");
        }
    }

    public Task<OperationResult<Unit>> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(statePath))
        {
            return Task.FromResult(OperationResult<Unit>.Success(new Unit()));
        }

        ProxyProcessState? state;
        try
        {
            state = JsonSerializer.Deserialize(
                File.ReadAllText(statePath),
                ProxyJsonContext.Default.ProxyProcessState);
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return Task.FromResult(OperationResult<Unit>.Failure("proxy.process.state", "Mihomo 进程状态无效"));
        }

        if (state is null)
        {
            return Task.FromResult(OperationResult<Unit>.Failure("proxy.process.state", "Mihomo 进程状态无效"));
        }

        if (platform.IsRunning(state.ProcessId))
        {
            var actualPath = platform.GetExecutablePath(state.ProcessId);
            if (actualPath is null || !Path.GetFullPath(actualPath).Equals(Path.GetFullPath(state.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(OperationResult<Unit>.Failure("proxy.process.identity", "进程身份不匹配，已拒绝结束其他程序"));
            }

            platform.Terminate(state.ProcessId);
        }

        File.Delete(statePath);
        return Task.FromResult(OperationResult<Unit>.Success(new Unit()));
    }

    private void TerminateIfOwned(int processId, string expectedExecutable)
    {
        if (!platform.IsRunning(processId))
        {
            return;
        }

        var actualPath = platform.GetExecutablePath(processId);
        if (actualPath is not null && Path.GetFullPath(actualPath).Equals(expectedExecutable, StringComparison.OrdinalIgnoreCase))
        {
            platform.Terminate(processId);
        }
    }

}

public sealed class SystemProxyProcessPlatform : IProxyProcessPlatform
{
    public async Task<bool> IsLoopbackPortOpenAsync(int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(400));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public int Start(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 Mihomo");
        return process.Id;
    }

    public bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public string? GetExecutablePath(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    public void Terminate(int processId)
    {
        using var process = Process.GetProcessById(processId);
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
    }
}
