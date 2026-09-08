using System.IO;
using System.Windows;
using CodexDeepSeekSetup.Windows.Packages;
using CodexDeepSeekSetup.Windows.Processes;
using CodexDeepSeekSetup.Windows.Security;

namespace CodexDeepSeekSetup.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args is ["elevated", "appx-install", _])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var runner = new SystemProcessRunner();
            var operations = new DefaultRestrictedOperations(
                new WindowsCredentialStore(),
                new CodexPackageVerifier(new PowerShellSignatureVerifier(runner)),
                runner);
            var router = new RestrictedCommandRouter(operations, DesktopSetupActions.GetRequestDirectory());
            var exitCode = await router.ExecuteAsync(e.Args, TextWriter.Null, TextWriter.Null, CancellationToken.None);
            Shutdown(exitCode);
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
    }
}
