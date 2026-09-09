namespace CodexDeepSeekSetup.Windows.Storage;

public sealed record StorageDriveCandidate(
    string RootPath,
    string VolumeLabel,
    string DriveFormat,
    DriveType DriveType,
    bool IsReady,
    long AvailableFreeSpace);

public sealed record InstallDriveOption(
    string RootPath,
    string DisplayName,
    long AvailableFreeSpace,
    bool IsSystemDrive,
    bool IsDefault);

public sealed class StorageSelectionService
{
    public const long MinimumInstallFreeBytes = 3L * 1024 * 1024 * 1024;

    public IReadOnlyList<InstallDriveOption> GetInstallDrives()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var systemRoot = Path.GetPathRoot(systemDirectory) ?? @"C:\";
        var candidates = DriveInfo.GetDrives().Select(drive =>
        {
            try
            {
                return new StorageDriveCandidate(
                    drive.RootDirectory.FullName,
                    drive.IsReady ? drive.VolumeLabel : string.Empty,
                    drive.IsReady ? drive.DriveFormat : string.Empty,
                    drive.DriveType,
                    drive.IsReady,
                    drive.IsReady ? drive.AvailableFreeSpace : 0);
            }
            catch
            {
                return new StorageDriveCandidate(
                    drive.RootDirectory.FullName,
                    string.Empty,
                    string.Empty,
                    drive.DriveType,
                    false,
                    0);
            }
        });
        return SelectInstallDrives(candidates, systemRoot);
    }

    public static IReadOnlyList<InstallDriveOption> SelectInstallDrives(
        IEnumerable<StorageDriveCandidate> candidates,
        string systemRoot)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var normalizedSystemRoot = NormalizeRoot(systemRoot);
        var eligible = candidates
            .Where(candidate => candidate.IsReady &&
                candidate.DriveType == DriveType.Fixed &&
                string.Equals(candidate.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase) &&
                candidate.AvailableFreeSpace >= MinimumInstallFreeBytes)
            .Select(candidate => new
            {
                Candidate = candidate,
                Root = NormalizeRoot(candidate.RootPath)
            })
            .OrderBy(item => item.Root, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (eligible.Length == 0)
        {
            return [];
        }

        var preferred = eligible.FirstOrDefault(item =>
                string.Equals(item.Root, @"D:\", StringComparison.OrdinalIgnoreCase))
            ?? eligible.FirstOrDefault(item =>
                string.Equals(item.Root, normalizedSystemRoot, StringComparison.OrdinalIgnoreCase))
            ?? eligible[0];

        return eligible.Select(item =>
        {
            var label = string.IsNullOrWhiteSpace(item.Candidate.VolumeLabel)
                ? item.Root
                : $"{item.Candidate.VolumeLabel} ({item.Root.TrimEnd('\\')})";
            var free = item.Candidate.AvailableFreeSpace / (1024d * 1024 * 1024);
            return new InstallDriveOption(
                item.Root,
                $"{label} · 可用 {free:F1} GB",
                item.Candidate.AvailableFreeSpace,
                string.Equals(item.Root, normalizedSystemRoot, StringComparison.OrdinalIgnoreCase),
                string.Equals(item.Root, preferred.Root, StringComparison.OrdinalIgnoreCase));
        }).ToArray();
    }

    public static string ResolveDefaultDownloadDirectory(
        string executableDirectory,
        string fallbackDirectory,
        Func<string, bool>? canWrite = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackDirectory);
        var adjacent = Path.Combine(Path.GetFullPath(executableDirectory), "Downloads");
        return (canWrite ?? CanWriteDirectory)(adjacent)
            ? adjacent
            : Path.GetFullPath(fallbackDirectory);
    }

    public static bool IsDriveRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.Length == 3 &&
            char.IsLetter(path[0]) &&
            path[1] == ':' &&
            path[2] == '\\';
    }

    private static bool CanWriteDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
            using (File.Create(probe))
            {
            }
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeRoot(string path)
    {
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            return $"{char.ToUpperInvariant(path[0])}:\\";
        }
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("路径不是磁盘根目录。", nameof(path));
        }
        return root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
    }
}
