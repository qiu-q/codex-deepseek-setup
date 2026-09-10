using System.Text.Json.Serialization;

namespace CodexDeepSeekSetup.Windows.Proxy;

[JsonSerializable(typeof(UserProxySnapshot))]
[JsonSerializable(typeof(ProxyProcessState))]
internal sealed partial class ProxyJsonContext : JsonSerializerContext;

internal sealed record ProxyProcessState(int ProcessId, string ExecutablePath, int Port);
