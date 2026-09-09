using CodexDeepSeekSetup.Windows.Storage;

namespace CodexDeepSeekSetup.Windows.Tests.Storage;

public sealed class StorageSelectionServiceTests
{
    [Fact]
    public void SelectInstallDrives_PrefersEligibleDDrive()
    {
        var drives = new[]
        {
            new StorageDriveCandidate(@"C:\", "System", "NTFS", DriveType.Fixed, true, 20L * 1024 * 1024 * 1024),
            new StorageDriveCandidate(@"D:\", "Data", "NTFS", DriveType.Fixed, true, 50L * 1024 * 1024 * 1024)
        };

        var result = StorageSelectionService.SelectInstallDrives(drives, @"C:\");

        Assert.Equal(2, result.Count);
        Assert.True(result.Single(item => item.RootPath == @"D:\").IsDefault);
        Assert.False(result.Single(item => item.RootPath == @"C:\").IsDefault);
    }

    [Fact]
    public void SelectInstallDrives_FallsBackToSystemDriveWhenDIsNotNtfs()
    {
        var drives = new[]
        {
            new StorageDriveCandidate(@"C:\", "System", "NTFS", DriveType.Fixed, true, 20L * 1024 * 1024 * 1024),
            new StorageDriveCandidate(@"D:\", "USB", "exFAT", DriveType.Fixed, true, 50L * 1024 * 1024 * 1024)
        };

        var result = StorageSelectionService.SelectInstallDrives(drives, @"C:\");

        Assert.Single(result);
        Assert.Equal(@"C:\", result[0].RootPath);
        Assert.True(result[0].IsDefault);
    }

    [Fact]
    public void ResolveDefaultDownloadDirectory_UsesExecutableDownloadsWhenWritable()
    {
        var executableDirectory = Path.Combine(Path.GetTempPath(), "Setup");
        var expected = Path.Combine(executableDirectory, "Downloads");
        var selected = StorageSelectionService.ResolveDefaultDownloadDirectory(
            executableDirectory,
            Path.Combine(Path.GetTempPath(), "Fallback"),
            path => path == expected);

        Assert.Equal(expected, selected);
    }

    [Fact]
    public void ResolveDefaultDownloadDirectory_FallsBackWhenExecutableDirectoryIsReadOnly()
    {
        var fallback = Path.Combine(Path.GetTempPath(), "Fallback");
        var selected = StorageSelectionService.ResolveDefaultDownloadDirectory(
            Path.Combine(Path.GetTempPath(), "ReadOnly"),
            fallback,
            _ => false);

        Assert.Equal(fallback, selected);
    }

    [Theory]
    [InlineData(@"C:", false)]
    [InlineData(@"C:\folder", false)]
    [InlineData(@"C:\", true)]
    [InlineData(@"d:\", true)]
    public void IsDriveRoot_AcceptsOnlyCanonicalWindowsDriveRoots(string path, bool expected)
    {
        Assert.Equal(expected, StorageSelectionService.IsDriveRoot(path));
    }
}
