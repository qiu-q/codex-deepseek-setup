using CodexDeepSeekSetup.Windows.Proxy;

if (!OperatingSystem.IsWindows())
{
    return 10;
}

var command = args.Length == 1 ? args[0].ToLowerInvariant() : string.Empty;
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var lifecycle = ProxyLifecycleService.CreateDefault(http);
var result = command switch
{
    "start" => await lifecycle.StartSavedAsync(CancellationToken.None),
    "stop" => await lifecycle.DisableAsync(false, false, false, CancellationToken.None),
    "repair" => await lifecycle.DisableAsync(false, false, false, CancellationToken.None),
    "remove" => await lifecycle.DisableAsync(true, true, false, CancellationToken.None),
    _ => null
};

if (result is null)
{
    Console.Error.WriteLine("Usage: CodexDeepSeekSetup.NetworkHelper.exe start|stop|repair|remove");
    return 2;
}
if (!result.IsSuccess)
{
    Console.Error.WriteLine(result.ErrorMessage);
    return 1;
}
return 0;
