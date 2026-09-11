using CodexDeepSeekSetup.Core.Security;

namespace CodexDeepSeekSetup.Core.Tests.Security;

public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("Authorization: Bearer sk-abc123456789", "Authorization: Bearer [REDACTED]")]
    [InlineData("key=sk-abc123456789", "key=[REDACTED]")]
    [InlineData("GET https://provider.example/sub?token=secret-value&user=42", "GET https://provider.example/sub?[REDACTED]")]
    [InlineData("https://provider.example/sub#access_token=secret-value", "https://provider.example/sub#[REDACTED]")]
    [InlineData("no secret here", "no secret here")]
    [InlineData(null, "")]
    public void Redact_RemovesBearerAndDeepSeekKeys(string? input, string expected)
    {
        Assert.Equal(expected, SecretRedactor.Redact(input));
    }
}
