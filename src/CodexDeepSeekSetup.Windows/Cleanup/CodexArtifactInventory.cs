using CodexDeepSeekSetup.Windows.Packages;

namespace CodexDeepSeekSetup.Windows.Cleanup;

public static class CodexArtifactIds
{
    public const string Package = "package";
    public const string CodexHome = "codex-home";
    public const string ExternalCodexHome = "external-codex-home";
    public const string AppLocalData = "app-local-data";
    public const string DesktopRuntime = "desktop-runtime";
    public const string Cli = "cli";
    public const string Portable = "portable";
    public const string AssistantData = "assistant-data";
    public const string AssistantInstall = "assistant-install";
    public const string Downloads = "downloads";
    public const string BundledPayload = "bundled-payload";
    public const string Credential = "credential";
    public const string EnvironmentVariable = "environment-variable";
    public const string AssistantSelf = "assistant-self";
}

public sealed record CodexArtifact(
    string Id,
    string DisplayName,
    string Location,
    long? SizeBytes,
    bool DefaultSelected,
    bool RequiresAdministrator,
    bool IsDestructiveUserData);

public sealed class CodexArtifactInventory(CleanupRoots roots)
{
    private static readonly string[] PayloadFileNames =
    [
        "ChatGPT-x64.msix",
        "ChatGPT-x64.msix.partial",
        "ChatGPT-x64.msix.part",
        "ChatGPT-License.xml",
        "ChatGPT-License.xml.partial",
        "ChatGPT-License.xml.part"
    ];

    public Task<IReadOnlyList<CodexArtifact>> ScanAsync(
        string downloadDirectory,
        CodexPackageStatus packageStatus,
        bool credentialExists,
        string? codexCliPath,
        CancellationToken cancellationToken)
        => ScanAsync(
            downloadDirectory,
            packageStatus,
            credentialExists,
            codexCliPath,
            effectiveCodexHome: null,
            cancellationToken);

    public Task<IReadOnlyList<CodexArtifact>> ScanAsync(
        string downloadDirectory,
        CodexPackageStatus packageStatus,
        bool credentialExists,
        string? codexCliPath,
        string? effectiveCodexHome,
        CancellationToken cancellationToken)
    {
        roots.Validate();
        var items = new List<CodexArtifact>();
        if (packageStatus.IsInstalled)
        {
            items.Add(new CodexArtifact(
                CodexArtifactIds.Package,
                $"OpenAI Codex 系统包 {packageStatus.Version}（所有用户及预配）",
                packageStatus.InstallLocation,
                null,
                false,
                true,
                true));
        }

        try
        {
            AddDirectory(
                items,
                CodexArtifactIds.CodexHome,
                "Codex 配置、插件与会话",
                roots.ResolveCodexHome(effectiveCodexHome),
                false,
                true);
        }
        catch (InvalidOperationException)
        {
            items.Add(new CodexArtifact(
                CodexArtifactIds.ExternalCodexHome,
                "外部 CODEX_HOME（仅显示，不自动删除）",
                effectiveCodexHome ?? string.Empty,
                null,
                false,
                false,
                true));
        }
        AddDirectory(
            items,
            CodexArtifactIds.AppLocalData,
            "Codex Windows 应用本地数据",
            Path.Combine(roots.LocalAppData, "Packages", "OpenAI.Codex_2p2nqsd0c76g0"),
            false,
            true);
        AddDirectory(
            items,
            CodexArtifactIds.DesktopRuntime,
            "Codex 桌面运行时与本地缓存",
            Path.Combine(roots.LocalAppData, "OpenAI", "Codex"),
            false,
            true);
        AddDirectory(items, CodexArtifactIds.Cli, "安装助手创建的 Codex CLI", roots.CliRoot, true, false);
        AddDirectory(items, CodexArtifactIds.Portable, "实验性 Codex 解包目录", roots.PortableRoot, true, false);
        AddAssistantState(items);
        AddDirectory(items, CodexArtifactIds.AssistantInstall, "安装助手辅助程序", roots.AssistantInstallRoot, true, false);

        var payloadFiles = PayloadFileNames
            .Select(name => Path.Combine(downloadDirectory, name))
            .Where(File.Exists)
            .ToArray();
        if (payloadFiles.Length > 0)
        {
            items.Add(new CodexArtifact(
                CodexArtifactIds.Downloads,
                "下载的官方安装文件",
                downloadDirectory,
                payloadFiles.Sum(path => TryGetFileSize(path)),
                true,
                false,
                false));
        }

        if (credentialExists)
        {
            items.Add(new CodexArtifact(
                CodexArtifactIds.Credential,
                "DeepSeek API Key Windows 凭据",
                "Windows 凭据管理器（不会显示 Key）",
                null,
                false,
                false,
                true));
        }

        if (!string.IsNullOrWhiteSpace(codexCliPath))
        {
            items.Add(new CodexArtifact(
                CodexArtifactIds.EnvironmentVariable,
                "CODEX_CLI_PATH 用户环境变量",
                codexCliPath,
                null,
                true,
                false,
                false));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<CodexArtifact>>(items);
    }

    private static void AddDirectory(
        ICollection<CodexArtifact> items,
        string id,
        string displayName,
        string path,
        bool defaultSelected,
        bool destructiveUserData)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        items.Add(new CodexArtifact(
            id,
            displayName,
            path,
            TryGetDirectorySize(path),
            defaultSelected,
            false,
            destructiveUserData));
    }

    private void AddAssistantState(ICollection<CodexArtifact> items)
    {
        var stateFile = Path.Combine(roots.AssistantDataRoot, "install-state.json");
        string[] temporaryFiles;
        try
        {
            temporaryFiles = Directory.Exists(roots.AssistantDataRoot)
                ? Directory.EnumerateFiles(roots.AssistantDataRoot, "install-state.json.*.tmp", SearchOption.TopDirectoryOnly).ToArray()
                : [];
        }
        catch
        {
            temporaryFiles = [];
        }
        if (!File.Exists(stateFile) && temporaryFiles.Length == 0)
        {
            return;
        }

        var size = (File.Exists(stateFile) ? TryGetFileSize(stateFile) : 0) +
            temporaryFiles.Sum(TryGetFileSize);
        items.Add(new CodexArtifact(
            CodexArtifactIds.AssistantData,
            "安装助手安装记录",
            stateFile,
            size,
            true,
            false,
            false));
    }

    private static long? TryGetDirectorySize(string path)
    {
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                total += TryGetFileSize(file);
            }
            return total;
        }
        catch
        {
            return null;
        }
    }

    private static long TryGetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }
}
