using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using CodexDeepSeekSetup.Core.Results;
using Microsoft.Win32;

namespace CodexDeepSeekSetup.Windows.Proxy;

public interface IUserProxyRegistry
{
    ProxyRegistryValue Read(string name);
    void Write(string name, ProxyRegistryValue value);
}

public interface IProxySettingsBroadcaster
{
    void Broadcast();
}

public sealed class WindowsUserProxyManager(
    IUserProxyRegistry registry,
    IProxySettingsBroadcaster broadcaster,
    string recoveryMarkerPath)
{
    private static readonly string[] ValueNames = ["ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL"];

    public Task<OperationResult<Unit>> CaptureAndApplyAsync(int port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (port is < 1 or > 65535)
        {
            return Task.FromResult(OperationResult<Unit>.Failure("proxy.settings.port", "代理端口无效"));
        }

        if (File.Exists(recoveryMarkerPath))
        {
            return Task.FromResult(OperationResult<Unit>.Failure("proxy.settings.stale", "检测到未恢复的系统代理设置，请先执行修复"));
        }

        try
        {
            var snapshot = new UserProxySnapshot(
                registry.Read(ValueNames[0]),
                registry.Read(ValueNames[1]),
                registry.Read(ValueNames[2]),
                registry.Read(ValueNames[3]));
            WriteSnapshot(snapshot);
            registry.Write("ProxyEnable", ProxyRegistryValue.Dword(1));
            registry.Write("ProxyServer", ProxyRegistryValue.String($"127.0.0.1:{port}"));
            registry.Write("ProxyOverride", ProxyRegistryValue.String("<local>"));
            registry.Write("AutoConfigURL", ProxyRegistryValue.Missing);
            broadcaster.Broadcast();
            return Task.FromResult(OperationResult<Unit>.Success(new Unit()));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return Task.FromResult(OperationResult<Unit>.Failure("proxy.settings.apply", "无法保存或应用当前用户的系统代理设置"));
        }
    }

    public Task<OperationResult<Unit>> RestoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(recoveryMarkerPath))
        {
            return Task.FromResult(OperationResult<Unit>.Success(new Unit()));
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<UserProxySnapshot>(File.ReadAllText(recoveryMarkerPath));
            if (snapshot is null)
            {
                return Task.FromResult(OperationResult<Unit>.Failure("proxy.settings.recovery", "系统代理恢复记录无效"));
            }

            registry.Write("ProxyEnable", snapshot.ProxyEnable);
            registry.Write("ProxyServer", snapshot.ProxyServer);
            registry.Write("ProxyOverride", snapshot.ProxyOverride);
            registry.Write("AutoConfigURL", snapshot.AutoConfigUrl);
            broadcaster.Broadcast();
            File.Delete(recoveryMarkerPath);
            return Task.FromResult(OperationResult<Unit>.Success(new Unit()));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return Task.FromResult(OperationResult<Unit>.Failure("proxy.settings.restore", "系统代理设置恢复失败，将在下次启动时重试"));
        }
    }

    public Task<OperationResult<Unit>> RepairIfStaleAsync(CancellationToken cancellationToken) =>
        RestoreAsync(cancellationToken);

    private void WriteSnapshot(UserProxySnapshot snapshot)
    {
        var fullPath = Path.GetFullPath(recoveryMarkerPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var partial = fullPath + ".partial";
        File.WriteAllText(partial, JsonSerializer.Serialize(snapshot));
        File.Move(partial, fullPath, overwrite: true);
    }
}

[SupportedOSPlatform("windows")]
public sealed class RegistryUserProxyRegistry : IUserProxyRegistry
{
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public ProxyRegistryValue Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: false);
        var value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value switch
        {
            null => ProxyRegistryValue.Missing,
            int number => ProxyRegistryValue.Dword(number),
            _ => ProxyRegistryValue.String(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))
        };
    }

    public void Write(string name, ProxyRegistryValue value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(InternetSettingsKey, writable: true);
        if (!value.Exists)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else if (value.DwordValue is int number)
        {
            key.SetValue(name, number, RegistryValueKind.DWord);
        }
        else
        {
            key.SetValue(name, value.StringValue ?? string.Empty, RegistryValueKind.String);
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsProxySettingsBroadcaster : IProxySettingsBroadcaster
{
    private const uint WmSettingChange = 0x001A;
    private static readonly IntPtr HwndBroadcast = new(0xffff);

    public void Broadcast()
    {
        _ = InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        _ = InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
        _ = SendMessageTimeout(
            HwndBroadcast,
            WmSettingChange,
            IntPtr.Zero,
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings",
            0x0002,
            3000,
            out _);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int bufferLength);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        IntPtr wordParameter,
        string longParameter,
        uint flags,
        uint timeout,
        out IntPtr result);
}
