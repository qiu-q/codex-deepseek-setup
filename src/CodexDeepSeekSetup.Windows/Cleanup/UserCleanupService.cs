using System.Runtime.Versioning;
using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Processes;
using CodexDeepSeekSetup.Windows.Security;

namespace CodexDeepSeekSetup.Windows.Cleanup;

public interface IUserEnvironment
{
    string? GetCodexCliPath();
    void SetCodexCliPath(string? value);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsUserEnvironment : IUserEnvironment
{
    public string? GetCodexCliPath() =>
        Environment.GetEnvironmentVariable("CODEX_CLI_PATH", EnvironmentVariableTarget.User);

    public void SetCodexCliPath(string? value)
    {
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", value, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", value, EnvironmentVariableTarget.Process);
    }
}

public sealed class UserCleanupService(
    ISecretStore secretStore,
    IUserEnvironment userEnvironment,
    CleanupRoots roots)
{
    public Task<OperationResult<Unit>> CleanAsync(
        string? previousCodexCliPath,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        try
        {
            roots.Validate();
        }
        catch (InvalidOperationException exception)
        {
            return Task.FromResult(OperationResult<Unit>.Failure("cleanup.path.rejected", exception.Message));
        }

        var credential = secretStore.Delete(CredentialTargets.DeepSeekApiKey);
        if (!credential.IsSuccess)
        {
            failures.Add("DeepSeek Windows 凭据");
        }

        try
        {
            var current = userEnvironment.GetCodexCliPath();
            if (!string.IsNullOrWhiteSpace(previousCodexCliPath) &&
                !roots.IsManagedCliPath(previousCodexCliPath))
            {
                userEnvironment.SetCodexCliPath(previousCodexCliPath);
            }
            else if (!string.IsNullOrWhiteSpace(current) && roots.IsManagedCliPath(current))
            {
                userEnvironment.SetCodexCliPath(null);
            }
        }
        catch
        {
            failures.Add("CODEX_CLI_PATH 环境变量");
        }

        foreach (var directory in roots.ManagedRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                failures.Add(directory);
            }
        }

        return Task.FromResult(failures.Count == 0
            ? OperationResult<Unit>.Success(default)
            : OperationResult<Unit>.Failure(
                "cleanup.user.incomplete",
                "以下内容未能删除：" + string.Join("；", failures)));
    }
}
