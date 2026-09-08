using System.IO.Compression;
using CodexDeepSeekSetup.Windows.Packages;

namespace CodexDeepSeekSetup.Windows.Tests.Packages;

public sealed class CodexPackageVerifierTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codex-package-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VerifyAsync_AcceptsExpectedSignedX64PackageAndOfflineLicense()
    {
        var (msix, license) = CreatePayload("OpenAI.Codex", "x64");
        var verifier = new CodexPackageVerifier(new AlwaysValidSignatureVerifier(), "8wekyb3d8bbwe");

        var result = await verifier.VerifyAsync(msix, license, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("OpenAI.Codex", result.Value!.PackageName);
        Assert.Equal("x64", result.Value.Architecture);
        Assert.Equal("9PLM9XGG6VKS", result.Value.ProductId);
    }

    [Theory]
    [InlineData("Other.Package", "x64")]
    [InlineData("OpenAI.Codex", "arm64")]
    public async Task VerifyAsync_RejectsUnexpectedIdentityOrArchitecture(string packageName, string architecture)
    {
        var (msix, license) = CreatePayload(packageName, architecture);
        var verifier = new CodexPackageVerifier(new AlwaysValidSignatureVerifier(), "8wekyb3d8bbwe");

        var result = await verifier.VerifyAsync(msix, license, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("package.identity.invalid", result.ErrorCode);
    }

    [Fact]
    public void CalculatePublisherId_MatchesKnownMicrosoftPackageIdentity()
    {
        const string publisher = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";

        Assert.Equal("8wekyb3d8bbwe", PublisherIdCalculator.Calculate(publisher));
    }

    private (string Msix, string License) CreatePayload(string packageName, string architecture)
    {
        Directory.CreateDirectory(root);
        var msix = Path.Combine(root, $"{Guid.NewGuid():N}.msix");
        using (var archive = ZipFile.Open(msix, ZipArchiveMode.Create))
        {
            var manifest = archive.CreateEntry("AppxManifest.xml");
            using (var writer = new StreamWriter(manifest.Open()))
            {
                writer.Write($"""
                    <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                      <Identity Name="{packageName}" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" Version="1.0.0.0" ProcessorArchitecture="{architecture}" />
                    </Package>
                    """);
            }

            var signature = archive.CreateEntry("AppxSignature.p7x");
            using var signatureStream = signature.Open();
            signatureStream.Write([1, 2, 3]);
        }

        var license = Path.Combine(root, $"{Guid.NewGuid():N}.xml");
        File.WriteAllText(license, """
            <License xmlns="urn:schemas-microsoft-com:windows:store:licensing:ls">
              <Binding><ProductID>9PLM9XGG6VKS</ProductID><PFM>openai.codex_2p2nqsd0c76g0</PFM></Binding>
            </License>
            """);
        return (msix, license);
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
}
