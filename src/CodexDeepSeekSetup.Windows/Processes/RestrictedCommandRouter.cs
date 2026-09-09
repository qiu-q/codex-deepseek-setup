namespace CodexDeepSeekSetup.Windows.Processes;

public static class CredentialTargets
{
    public const string DeepSeekApiKey = "CodexDeepSeekSetup/DeepSeekApiKey";
}

public interface IRestrictedOperations
{
    Task<int> ReadCredentialAsync(
        string target,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken);

    Task<int> InstallAppxAsync(
        string requestFile,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken);

    Task<int> PrepareAppxVolumeAsync(
        string requestFile,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken);

    Task<int> RemoveCodexAsync(
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken);

    Task<int> StartServiceAsync(
        string serviceName,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken);

    Task<int> ResumeAsync(
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken);
}

public sealed class RestrictedCommandRouter
{
    public const int UsageError = 64;

    private static readonly HashSet<string> AllowedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "AppXSvc",
        "ClipSVC",
        "LicenseManager",
        "StateRepository"
    };

    private readonly IRestrictedOperations operations;
    private readonly string requestRoot;

    public RestrictedCommandRouter(IRestrictedOperations operations, string requestRoot)
    {
        this.operations = operations;
        this.requestRoot = Path.GetFullPath(requestRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    public Task<int> ExecuteAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args switch
        {
            ["credential", "read", "--target", CredentialTargets.DeepSeekApiKey] =>
                operations.ReadCredentialAsync(CredentialTargets.DeepSeekApiKey, output, error, cancellationToken),
            ["elevated", "appx-install", var requestFile] when IsOwnedRequestFile(requestFile) =>
                operations.InstallAppxAsync(Path.GetFullPath(requestFile), output, error, cancellationToken),
            ["elevated", "appx-prepare-volume", var requestFile] when IsOwnedRequestFile(requestFile) =>
                operations.PrepareAppxVolumeAsync(Path.GetFullPath(requestFile), output, error, cancellationToken),
            ["elevated", "appx-remove"] =>
                operations.RemoveCodexAsync(output, error, cancellationToken),
            ["elevated", "start-service", var serviceName] when AllowedServices.Contains(serviceName) =>
                operations.StartServiceAsync(serviceName, output, error, cancellationToken),
            ["resume"] => operations.ResumeAsync(output, error, cancellationToken),
            _ => Task.FromResult(UsageError)
        };
    }

    private bool IsOwnedRequestFile(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(requestRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath);
    }
}
