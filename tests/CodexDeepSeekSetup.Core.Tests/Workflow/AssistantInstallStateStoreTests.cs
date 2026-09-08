using CodexDeepSeekSetup.Core.Workflow;

namespace CodexDeepSeekSetup.Core.Tests.Workflow;

public sealed class AssistantInstallStateStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"codex-assistant-state-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_RoundTripsProvenanceWithoutSecretText()
    {
        var path = Path.Combine(directory, "install-state.json");
        var state = CreateState();
        var store = new AssistantInstallStateStore(path);

        await store.SaveAsync(state, default);

        Assert.Equal(state, await store.LoadAsync(default));
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_WhenStateDoesNotExist_ReturnsNull()
    {
        var store = new AssistantInstallStateStore(Path.Combine(directory, "missing.json"));

        Assert.Null(await store.LoadAsync(default));
    }

    [Fact]
    public async Task SaveAsync_WhenNewStateContainsSecret_PreservesOriginalState()
    {
        var path = Path.Combine(directory, "install-state.json");
        var original = CreateState();
        var store = new AssistantInstallStateStore(path);
        await store.SaveAsync(original, default);
        var unsafeState = original with { PreviousCodexCliPath = "sk-never-store-this" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(unsafeState, default));

        Assert.Equal(original, await store.LoadAsync(default));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static AssistantInstallState CreateState() => new(
        SchemaVersion: 1,
        CodexExistedBefore: false,
        InstallMode: "official",
        PreviousCodexCliPath: @"D:\tools\codex.exe",
        DeepSeekConfigured: true,
        DownloadCache: @"C:\Users\test\AppData\Local\CodexDeepSeekSetup\Downloads",
        PortableDirectory: null,
        CliDirectory: @"C:\Users\test\AppData\Local\Programs\OpenAI\Codex\bin",
        CredentialHelperPath: @"C:\Users\test\AppData\Local\Programs\CodexDeepSeekSetup\CodexDeepSeekSetup.Helper.exe",
        AssistantDirectory: @"D:\assistant");
}
