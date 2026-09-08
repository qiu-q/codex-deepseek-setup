namespace CodexDeepSeekSetup.Windows.Cleanup;

public sealed record CleanupRoots(
    string UserProfile,
    string LocalAppData,
    string Desktop,
    string CodexHome,
    string AssistantDataRoot,
    string AssistantInstallRoot,
    string CliRoot,
    string PortableRoot)
{
    public IReadOnlyList<string> ManagedRoots =>
    [
        CodexHome,
        PortableRoot,
        CliRoot,
        AssistantInstallRoot,
        AssistantDataRoot
    ];

    public static CleanupRoots ForCurrentUser()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new CleanupRoots(
            profile,
            local,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(profile, ".codex"),
            Path.Combine(local, "CodexDeepSeekSetup"),
            Path.Combine(local, "Programs", "CodexDeepSeekSetup"),
            Path.Combine(local, "Programs", "OpenAI", "Codex"),
            Path.Combine(local, "Programs", "OpenAI", "CodexPortable"));
    }

    public void Validate()
    {
        var profile = Normalize(UserProfile);
        var local = Normalize(LocalAppData);
        var desktop = Normalize(Desktop);
        var broadRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            profile,
            local,
            desktop,
            Normalize(Path.GetPathRoot(profile) ?? throw new InvalidOperationException("用户目录没有磁盘根路径。"))
        };

        var normalized = ManagedRoots.Select(Normalize).ToArray();
        if (normalized.Any(broadRoots.Contains) ||
            !IsStrictChild(normalized[0], profile) ||
            normalized.Skip(1).Any(path => !IsStrictChild(path, local)) ||
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
