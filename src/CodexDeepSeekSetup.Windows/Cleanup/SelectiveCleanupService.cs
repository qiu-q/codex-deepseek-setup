using CodexDeepSeekSetup.Core.Results;
using CodexDeepSeekSetup.Windows.Processes;
using CodexDeepSeekSetup.Windows.Security;

namespace CodexDeepSeekSetup.Windows.Cleanup;

public sealed class SelectiveCleanupService(
    ISecretStore secretStore,
    IUserEnvironment userEnvironment,
    CleanupRoots roots)
{
    private readonly Dictionary<string, string> knownDirectories = new(StringComparer.Ordinal)
    {
        [CodexArtifactIds.AppLocalData] = Path.Combine(roots.LocalAppData, "Packages", "OpenAI.Codex_2p2nqsd0c76g0"),
        [CodexArtifactIds.DesktopRuntime] = Path.Combine(roots.LocalAppData, "OpenAI", "Codex"),
        [CodexArtifactIds.Cli] = roots.CliRoot,
        [CodexArtifactIds.Portable] = roots.PortableRoot,
        [CodexArtifactIds.AssistantInstall] = roots.AssistantInstallRoot
    };

    private static readonly string[] PayloadFileNames =
    [
        "ChatGPT-x64.msix",
        "ChatGPT-x64.msix.partial",
        "ChatGPT-x64.msix.part",
        "ChatGPT-License.xml",
        "ChatGPT-License.xml.partial",
        "ChatGPT-License.xml.part"
    ];

    public Task<OperationResult<Unit>> CleanAsync(
        IReadOnlyCollection<string> selectedArtifactIds,
        string downloadDirectory,
        string? previousCodexCliPath,
        CancellationToken cancellationToken)
        => CleanAsync(
            selectedArtifactIds,
            downloadDirectory,
            previousCodexCliPath,
            effectiveCodexHome: null,
            cancellationToken);

    public Task<OperationResult<Unit>> CleanAsync(
        IReadOnlyCollection<string> selectedArtifactIds,
        string downloadDirectory,
        string? previousCodexCliPath,
        string? effectiveCodexHome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedArtifactIds);
        var selected = selectedArtifactIds.ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();
        try
        {
            roots.Validate();
        }
        catch (InvalidOperationException exception)
        {
            return Task.FromResult(OperationResult<Unit>.Failure("cleanup.path.rejected", exception.Message));
        }

        foreach (var (id, directory) in knownDirectories)
        {
            if (!selected.Contains(id))
            {
                continue;
            }

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

        if (selected.Contains(CodexArtifactIds.CodexHome))
        {
            try
            {
                var directory = roots.ResolveCodexHome(effectiveCodexHome);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failures.Add("CODEX_HOME");
            }
        }
        if (selected.Contains(CodexArtifactIds.ExternalCodexHome))
        {
            failures.Add("外部 CODEX_HOME（为防止误删，请手动处理）");
        }


        if (selected.Contains(CodexArtifactIds.AssistantData))
        {
            DeleteAssistantStateFiles(failures);
        }

        if (selected.Contains(CodexArtifactIds.Downloads))
        {
            foreach (var name in PayloadFileNames)
            {
                try
                {
                    var path = Path.Combine(downloadDirectory, name);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                    failures.Add(Path.Combine(downloadDirectory, name));
                }
            }
        }

        if (selected.Contains(CodexArtifactIds.Credential))
        {
            var credential = secretStore.Delete(CredentialTargets.DeepSeekApiKey);
            if (!credential.IsSuccess)
            {
                failures.Add("DeepSeek Windows 凭据");
            }
        }

        if (selected.Contains(CodexArtifactIds.EnvironmentVariable))
        {
            try
            {
                var current = userEnvironment.GetCodexCliPath();
                if (!string.IsNullOrWhiteSpace(previousCodexCliPath) && !roots.IsManagedCliPath(previousCodexCliPath))
                {
                    userEnvironment.SetCodexCliPath(previousCodexCliPath);
                }
                else if (!string.IsNullOrWhiteSpace(current))
                {
                    userEnvironment.SetCodexCliPath(null);
                }
            }
            catch
            {
                failures.Add("CODEX_CLI_PATH 环境变量");
            }
        }

        return Task.FromResult(failures.Count == 0
            ? OperationResult<Unit>.Success(default)
            : OperationResult<Unit>.Failure("cleanup.selected.incomplete", "以下内容未能删除：" + string.Join("；", failures)));
    }

    private void DeleteAssistantStateFiles(ICollection<string> failures)
    {
        var candidates = new List<string>
        {
            Path.Combine(roots.AssistantDataRoot, "install-state.json")
        };
        try
        {
            if (Directory.Exists(roots.AssistantDataRoot))
            {
                candidates.AddRange(Directory.EnumerateFiles(
                    roots.AssistantDataRoot,
                    "install-state.json.*.tmp",
                    SearchOption.TopDirectoryOnly));
            }
            foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            if (Directory.Exists(roots.AssistantDataRoot) &&
                !Directory.EnumerateFileSystemEntries(roots.AssistantDataRoot).Any())
            {
                Directory.Delete(roots.AssistantDataRoot);
            }
        }
        catch
        {
            failures.Add(Path.Combine(roots.AssistantDataRoot, "install-state.json"));
        }
    }
}
