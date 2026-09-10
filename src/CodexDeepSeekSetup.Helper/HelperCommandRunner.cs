namespace CodexDeepSeekSetup.Helper;

public interface IHelperCredentialReader
{
    string? Read(string target);
}

public interface IHelperFinalizer
{
    Task<bool> RunAsync(int parentPid, string directory, CancellationToken cancellationToken);
}

public sealed class HelperCommandRunner(IHelperCredentialReader credentialReader, IHelperFinalizer finalizer)
{
    public const string DeepSeekCredentialTarget = "CodexDeepSeekSetup/DeepSeekApiKey";
    public const int UsageError = 64;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args is ["credential", "read", "--target", DeepSeekCredentialTarget])
        {
            var secret = credentialReader.Read(DeepSeekCredentialTarget);
            if (string.IsNullOrWhiteSpace(secret))
            {
                error.Write("Windows 凭据中尚未保存 DeepSeek API Key。");
                return 1;
            }
            output.Write(secret);
            return 0;
        }

        if (args is ["finalize-cleanup", var parentText, var directory] &&
            int.TryParse(parentText, out var parentPid) && parentPid >= 0)
        {
            return await finalizer.RunAsync(parentPid, directory, cancellationToken).ConfigureAwait(false) ? 0 : 1;
        }

        return UsageError;
    }
}
