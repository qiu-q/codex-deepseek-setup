using System.IO.Compression;
using CodexDeepSeekSetup.Windows.Packages;
using CodexDeepSeekSetup.Windows.Processes;

namespace CodexDeepSeekSetup.Windows.Tests.Packages;

public sealed class PortableCodexInstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "portable-codex-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallAsync_ExtractsVerifiedOfficialPackageAndTestsBundledCli()
    {
        var (msix, license) = CreatePayload(includeTraversalEntry: false);
        var installer = new PortableCodexInstaller(
            new CodexPackageVerifier(new AlwaysValidSignatureVerifier(), "8wekyb3d8bbwe"),
            new SuccessfulProcessRunner());

        var result = await installer.InstallAsync(msix, license, Path.Combine(root, "install"), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(result.Value!.AppPath));
        Assert.True(File.Exists(result.Value.CliPath));
        Assert.EndsWith(Path.Combine("app", "ChatGPT.exe"), result.Value.AppPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallAsync_RejectsArchiveEntryOutsidePortableDirectory()
    {
        var (msix, license) = CreatePayload(includeTraversalEntry: true);
        var installer = new PortableCodexInstaller(
            new CodexPackageVerifier(new AlwaysValidSignatureVerifier(), "8wekyb3d8bbwe"),
            new SuccessfulProcessRunner());

        var result = await installer.InstallAsync(msix, license, Path.Combine(root, "install"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("portable.archive.unsafe", result.ErrorCode);
        Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
    }

    [Fact]
    public void TryRecover_UsesPersistedCliOnlyWhenItBelongsToPortableRoot()
    {
        var installRoot = Path.Combine(root, "install", "26.901.6511.0-existing");
        var appPath = Path.Combine(installRoot, "app", "ChatGPT.exe");
        var cliPath = Path.Combine(installRoot, "app", "resources", "codex.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(cliPath)!);
        File.WriteAllText(appPath, "app");
        File.WriteAllText(cliPath, "cli");

        var recovered = PortableCodexInstaller.TryRecover(Path.Combine(root, "install"), cliPath);
        var rejected = PortableCodexInstaller.TryRecover(Path.Combine(root, "other"), cliPath);

        Assert.NotNull(recovered);
        Assert.Equal(appPath, recovered.AppPath);
        Assert.Null(rejected);
    }

    private (string Msix, string License) CreatePayload(bool includeTraversalEntry)
    {
        Directory.CreateDirectory(root);
        var msix = Path.Combine(root, $"{Guid.NewGuid():N}.msix");
        using (var archive = ZipFile.Open(msix, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "AppxManifest.xml", """
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Identity Name="OpenAI.Codex" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" Version="26.901.6511.0" ProcessorArchitecture="x64" />
                </Package>
                """);
            WriteEntry(archive, "AppxSignature.p7x", "signature");
            WriteEntry(archive, "app/ChatGPT.exe", "app");
            WriteEntry(archive, "app/resources/codex.exe", "cli");
            WriteEntry(archive, "app/resources/codex-command-runner.exe", "runner");
            if (includeTraversalEntry)
            {
                WriteEntry(archive, "../outside.txt", "unsafe");
            }
        }

        var license = Path.Combine(root, $"{Guid.NewGuid():N}.xml");
        File.WriteAllText(license, """
            <License xmlns="urn:schemas-microsoft-com:windows:store:licensing:ls">
              <Binding><ProductID>9PLM9XGG6VKS</ProductID><PFM>openai.codex_2p2nqsd0c76g0</PFM></Binding>
            </License>
            """);
        return (msix, license);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private sealed class AlwaysValidSignatureVerifier : ISignatureVerifier
    {
        public Task<bool> IsValidAsync(string filePath, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class SuccessfulProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string?>? environment,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, "codex-cli 0.153.4", string.Empty));
    }
}
