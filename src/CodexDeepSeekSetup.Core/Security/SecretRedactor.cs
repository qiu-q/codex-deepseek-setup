using System.Text.RegularExpressions;

namespace CodexDeepSeekSetup.Core.Security;

public static partial class SecretRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var withoutBearer = BearerPattern().Replace(value, "$1[REDACTED]");
        var withoutKeys = DeepSeekKeyPattern().Replace(withoutBearer, "[REDACTED]");
        var withoutQuery = UrlQueryPattern().Replace(withoutKeys, "$1?[REDACTED]");
        return UrlFragmentPattern().Replace(withoutQuery, "$1#[REDACTED]");
    }

    [GeneratedRegex(@"(?i)(Authorization\s*:\s*Bearer\s+)\S+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex DeepSeekKeyPattern();

    [GeneratedRegex(@"(?i)([a-z][a-z0-9+.-]*://[^\s?#]+)\?[^\s#]*", RegexOptions.CultureInvariant)]
    private static partial Regex UrlQueryPattern();

    [GeneratedRegex(@"(?i)([a-z][a-z0-9+.-]*://[^\s#]+)#[^\s]*", RegexOptions.CultureInvariant)]
    private static partial Regex UrlFragmentPattern();
}
