using System.Runtime.Versioning;
using CodexDeepSeekSetup.Windows.Processes;

namespace CodexDeepSeekSetup.Windows.Packages;

[SupportedOSPlatform("windows")]
public sealed class PowerShellSignatureVerifier(IProcessRunner processRunner) : ISignatureVerifier
{
    private const string Script = "$s=Get-AuthenticodeSignature -LiteralPath $env:CODEX_SETUP_VERIFY_PATH; if($s.Status -eq 'Valid'){exit 0}else{Write-Error $s.Status;exit 1}";

    public async Task<bool> IsValidAsync(string filePath, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", Script],
            new Dictionary<string, string?> { ["CODEX_SETUP_VERIFY_PATH"] = Path.GetFullPath(filePath) },
            cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }
}
