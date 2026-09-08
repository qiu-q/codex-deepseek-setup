namespace CodexDeepSeekSetup.Windows.Packages;

public interface ISignatureVerifier
{
    Task<bool> IsValidAsync(string filePath, CancellationToken cancellationToken);
}
