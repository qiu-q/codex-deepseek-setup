using System.Text;

namespace CodexDeepSeekSetup.Windows.Security;

public static class CredentialBlobCodec
{
    public const int MaximumBlobBytes = 2560;

    public static byte[] Encode(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var bytes = Encoding.Unicode.GetBytes(secret);
        if (bytes.Length > MaximumBlobBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(secret), $"凭据不能超过 {MaximumBlobBytes / 2} 个 Unicode 字符。");
        }

        return bytes;
    }

    public static string Decode(ReadOnlySpan<byte> blob)
    {
        if (blob.Length == 0 || blob.Length > MaximumBlobBytes || blob.Length % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blob), "凭据数据长度无效。");
        }

        return Encoding.Unicode.GetString(blob);
    }
}
