using CodexDeepSeekSetup.Windows.Packages;
using CodexDeepSeekSetup.Windows.Processes;
using CodexDeepSeekSetup.Windows.Security;
using CodexDeepSeekSetup.Windows.Cleanup;

if (!OperatingSystem.IsWindows())
{
    return 1;
}

if (args is ["finalize-cleanup", var parentText, var appDirectory] &&
    int.TryParse(parentText, out var parentPid) &&
    parentPid >= 0)
{
    var executable = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(executable))
    {
        return 65;
    }

    var finalizer = new SelfDeleteFinalizer(executable);
    var result = await finalizer.RunAsync(parentPid, appDirectory, CancellationToken.None);
    return result.IsSuccess ? 0 : 1;
}

var requestRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "CodexDeepSeekSetup",
    "Requests");
var runner = new SystemProcessRunner();
var signatureVerifier = new PowerShellSignatureVerifier(runner);
var packageVerifier = new CodexPackageVerifier(signatureVerifier);
var operations = new DefaultRestrictedOperations(new WindowsCredentialStore(), packageVerifier, runner);
var router = new RestrictedCommandRouter(operations, requestRoot);
return await router.ExecuteAsync(args, Console.Out, Console.Error, CancellationToken.None);
