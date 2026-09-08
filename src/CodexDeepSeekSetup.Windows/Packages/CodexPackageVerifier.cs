using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.Windows.Packages;

public sealed record CodexPackageInfo(
    string PackageName,
    string Architecture,
    string Version,
    string Publisher,
    string ProductId);

public static class PublisherIdCalculator
{
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    public static string Calculate(string publisher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisher);
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        Span<char> output = stackalloc char[13];
        for (var group = 0; group < output.Length; group++)
        {
            var value = 0;
            for (var offset = 0; offset < 5; offset++)
            {
                value <<= 1;
                var bit = group * 5 + offset;
                if (bit < 64)
                {
                    value |= (hash[bit / 8] >> (7 - bit % 8)) & 1;
                }
            }
            output[group] = Alphabet[value];
        }
        return new string(output);
    }
}

public sealed class CodexPackageVerifier
{
    private const string ExpectedPackageName = "OpenAI.Codex";
    private const string ExpectedArchitecture = "x64";
    private const string ExpectedProductId = "9PLM9XGG6VKS";
    private const string ExpectedPackageFamily = "openai.codex_2p2nqsd0c76g0";
    private readonly ISignatureVerifier signatureVerifier;
    private readonly string expectedPublisherId;

    public CodexPackageVerifier(ISignatureVerifier signatureVerifier, string expectedPublisherId = "2p2nqsd0c76g0")
    {
        this.signatureVerifier = signatureVerifier;
        this.expectedPublisherId = expectedPublisherId;
    }

    public async Task<OperationResult<CodexPackageInfo>> VerifyAsync(
        string msixPath,
        string licensePath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(msixPath) || new FileInfo(msixPath).Length == 0)
            {
                return Failure("package.structure.invalid", "MSIX 文件不存在或为空。");
            }

            if (!File.Exists(licensePath) || new FileInfo(licensePath).Length == 0)
            {
                return Failure("package.license.invalid", "离线许可证不存在或为空。");
            }

            var identityResult = ReadIdentity(msixPath);
            if (!identityResult.IsSuccess)
            {
                return OperationResult<CodexPackageInfo>.Failure(identityResult.ErrorCode!, identityResult.ErrorMessage!);
            }

            var identity = identityResult.Value!;
            if (!string.Equals(identity.PackageName, ExpectedPackageName, StringComparison.Ordinal) ||
                !string.Equals(identity.Architecture, ExpectedArchitecture, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(PublisherIdCalculator.Calculate(identity.Publisher), expectedPublisherId, StringComparison.OrdinalIgnoreCase))
            {
                return Failure("package.identity.invalid", "安装包身份或处理器架构不是预期的官方 Codex x64 包。");
            }

            var licenseResult = ReadLicense(licensePath);
            if (!licenseResult.IsSuccess)
            {
                return OperationResult<CodexPackageInfo>.Failure(licenseResult.ErrorCode!, licenseResult.ErrorMessage!);
            }

            var license = licenseResult.Value!;
            if (!string.Equals(license.ProductId, ExpectedProductId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(license.PackageFamily, ExpectedPackageFamily, StringComparison.OrdinalIgnoreCase))
            {
                return Failure("package.license.invalid", "离线许可证不属于官方 Codex Windows 应用。");
            }

            if (!await signatureVerifier.IsValidAsync(msixPath, cancellationToken).ConfigureAwait(false))
            {
                return Failure("package.signature.invalid", "Windows 无法验证 MSIX 的数字签名。");
            }

            return OperationResult<CodexPackageInfo>.Success(identity with { ProductId = license.ProductId });
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return Failure("package.structure.invalid", "无法读取或解析安装文件。");
        }
    }

    private static OperationResult<CodexPackageInfo> ReadIdentity(string msixPath)
    {
        using var archive = ZipFile.OpenRead(msixPath);
        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName, "AppxManifest.xml", StringComparison.OrdinalIgnoreCase));
        var signatureEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName, "AppxSignature.p7x", StringComparison.OrdinalIgnoreCase));

        if (manifestEntry is null || signatureEntry is null || signatureEntry.Length == 0)
        {
            return OperationResult<CodexPackageInfo>.Failure(
                "package.structure.invalid",
                "MSIX 缺少清单或签名文件。");
        }

        using var manifestStream = manifestEntry.Open();
        var document = XDocument.Load(manifestStream);
        var identity = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "Identity");
        if (identity is null)
        {
            return OperationResult<CodexPackageInfo>.Failure("package.identity.invalid", "MSIX 清单缺少 Identity。");
        }

        return OperationResult<CodexPackageInfo>.Success(new CodexPackageInfo(
            identity.Attribute("Name")?.Value ?? string.Empty,
            identity.Attribute("ProcessorArchitecture")?.Value ?? string.Empty,
            identity.Attribute("Version")?.Value ?? string.Empty,
            identity.Attribute("Publisher")?.Value ?? string.Empty,
            string.Empty));
    }

    private static OperationResult<(string ProductId, string PackageFamily)> ReadLicense(string licensePath)
    {
        var document = XDocument.Load(licensePath);
        var productId = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "ProductID")?.Value;
        var packageFamily = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "PFM")?.Value;

        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(packageFamily))
        {
            return OperationResult<(string, string)>.Failure("package.license.invalid", "许可证内容不完整。");
        }

        return OperationResult<(string, string)>.Success((productId, packageFamily));
    }

    private static OperationResult<CodexPackageInfo> Failure(string code, string message) =>
        OperationResult<CodexPackageInfo>.Failure(code, message);
}
