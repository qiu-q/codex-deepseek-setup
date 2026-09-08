using CodexDeepSeekSetup.Windows.Cleanup;

namespace CodexDeepSeekSetup.Windows.Tests.Cleanup;

public sealed class SelfDeleteFinalizerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"codex-self-delete-{Guid.NewGuid():N}");

    [Fact]
    public async Task RunAsync_DeletesPublishedFilesButPreservesUnknownNeighbor()
    {
        var appDirectory = CreatePublishedLayout();
        await File.WriteAllTextAsync(Path.Combine(appDirectory, "my-notes.txt"), "keep");
        var finalizer = CreateFinalizer();

        var result = await finalizer.RunAsync(parentPid: 0, appDirectory, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(appDirectory, "my-notes.txt")));
        Assert.All(
            SelfDeleteFinalizer.PublishedFileNames,
            name => Assert.False(File.Exists(Path.Combine(appDirectory, name))));
    }

    [Fact]
    public async Task RunAsync_RemovesApplicationDirectoryWhenNoUnknownFilesRemain()
    {
        var appDirectory = CreatePublishedLayout();
        var finalizer = CreateFinalizer();

        var result = await finalizer.RunAsync(parentPid: 0, appDirectory, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(Directory.Exists(appDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private string CreatePublishedLayout()
    {
        var directory = Path.Combine(root, "assistant");
        Directory.CreateDirectory(directory);
        foreach (var name in SelfDeleteFinalizer.PublishedFileNames)
        {
            File.WriteAllText(Path.Combine(directory, name), "published");
        }
        return directory;
    }

    private static SelfDeleteFinalizer CreateFinalizer() => new(
        currentExecutablePath: Path.Combine(Path.GetTempPath(), $"cleanup-{Guid.NewGuid():N}.exe"),
        waitForProcess: (_, _) => Task.CompletedTask,
        scheduleSelfDelete: _ => { });
}
