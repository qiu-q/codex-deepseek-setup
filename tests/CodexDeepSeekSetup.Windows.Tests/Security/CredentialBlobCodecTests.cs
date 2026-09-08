using CodexDeepSeekSetup.Windows.Security;

namespace CodexDeepSeekSetup.Windows.Tests.Security;

public sealed class CredentialBlobCodecTests
{
    [Fact]
    public void EncodeAndDecode_RoundTripsUnicodeSecretWithoutNullTerminator()
    {
        const string secret = "sk-密钥-abc123456789";

        var bytes = CredentialBlobCodec.Encode(secret);

        Assert.Equal(secret, CredentialBlobCodec.Decode(bytes));
        Assert.Equal(secret.Length * sizeof(char), bytes.Length);
        Assert.False(bytes[^2] == 0 && bytes[^1] == 0);
    }

    [Fact]
    public void Encode_RejectsCredentialLargerThanWindowsLimit()
    {
        var secret = new string('a', 1281);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => CredentialBlobCodec.Encode(secret));

        Assert.Equal("secret", error.ParamName);
    }
}
