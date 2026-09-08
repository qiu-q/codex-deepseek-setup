using CodexDeepSeekSetup.Windows.Packages;
using CodexDeepSeekSetup.Windows.Processes;
using CodexDeepSeekSetup.Windows.Security;

if (!OperatingSystem.IsWindows())
{
    return 1;
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
