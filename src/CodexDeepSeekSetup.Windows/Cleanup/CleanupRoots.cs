namespace CodexDeepSeekSetup.Windows.Cleanup;

public sealed record CleanupRoots(
    string UserProfile,
    string LocalAppData,
    string Desktop,
    string CodexHome,
    string AssistantDataRoot,
    string AssistantInstallRoot,
    string CliRoot,
    string PortableRoot,
    string CommonDocuments,
    string SharedAssistantRoot)
{
    public IReadOnlyList<string> ManagedRoots =>
    [
        CodexHome,
        PortableRoot,
        CliRoot,
        AssistantInstallRoot,
        AssistantDataRoot,
        SharedAssistantRoot
    ];

    public static CleanupRoots ForCurrentUser()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var commonDocuments = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
        return new CleanupRoots(
            profile,
            local,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(profile, ".codex"),
            Path.Combine(local, "CodexDeepSeekSetup"),
            Path.Combine(local, "Programs", "CodexDeepSeekSetup"),
            Path.Combine(local, "Programs", "OpenAI", "Codex"),
            Path.Combine(local, "Programs", "OpenAI", "CodexPortable"),
            commonDocuments,
            Path.Combine(commonDocuments, "CodexDeepSeekSetup"));
    }

    public void Validate()
    {
        var profile = Normalize(UserProfile);
        var local = Normalize(LocalAppData);
        var desktop = Normalize(Desktop);
        var commonDocuments = Normalize(CommonDocuments);
        var broadRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            profile,
            local,
            desktop,
            commonDocuments,
            Normalize(Path.GetPathRoot(profile) ?? throw new InvalidOperationException("用户目录没有磁盘根路径。"))
        };

        var normalized = ManagedRoots.Select(Normalize).ToArray();
        var localRoots = new[] { PortableRoot, CliRoot, AssistantInstallRoot, AssistantDataRoot }
            .Select(Normalize);
        if (normalized.Any(broadRoots.Contains) ||
            !IsStrictChild(Normalize(CodexHome), profile) ||
            localRoots.Any(path => !IsStrictChild(path, local)) ||
            !IsStrictChild(Normalize(SharedAssistantRoot), commonDocuments) ||
            normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new InvalidOperationException("清理目录超出允许的用户数据范围。");
        }
    }

    public bool IsManagedCliPath(string path)
    {
        try
        {
            var candidate = Normalize(path);
            var cliRoot = Normalize(CliRoot);
            var portableRoot = Normalize(PortableRoot);
            return IsSameOrChild(candidate, cliRoot) || IsSameOrChild(candidate, portableRoot);
        }
        catch
        {
            return false;
        }
    }

    public string ResolveCodexHome(string? configuredPath)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredPath)
            ? Normalize(CodexHome)
            : Normalize(Environment.ExpandEnvironmentVariables(configuredPath));
        var profile = Normalize(UserProfile);
        var local = Normalize(LocalAppData);
        if (!IsStrictChild(candidate, profile) && !IsStrictChild(candidate, local))
        {
            throw new InvalidOperationException("CODEX_HOME 不在当前用户目录内，安装助手不会自动删除该外部路径。");
        }
        return candidate;
    }

    private static bool IsStrictChild(string child, string parent) =>
        !string.Equals(child, parent, StringComparison.OrdinalIgnoreCase) && IsSameOrChild(child, parent);

    private static bool IsSameOrChild(string child, string parent)
    {
        var prefix = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return string.Equals(child, parent, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("清理目录不能为空。");
        }

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
            ? full
            : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
