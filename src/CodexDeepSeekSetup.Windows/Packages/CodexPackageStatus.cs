using System.Text.Json;

namespace CodexDeepSeekSetup.Windows.Packages;

public sealed record CodexPackageStatus(
    bool IsInstalled,
    string Version,
    string PackageFullName,
    string InstallLocation,
    string DriveRoot,
    string Status)
{
    public static CodexPackageStatus NotInstalled { get; } =
        new(false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}

public static class CodexPackageStatusParser
{
    public static CodexPackageStatus Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CodexPackageStatus.NotInstalled;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().FirstOrDefault()
            : document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return CodexPackageStatus.NotInstalled;
        }

        var location = GetString(root, "InstallLocation");
        var driveRoot = location.Length >= 2 && char.IsLetter(location[0]) && location[1] == ':'
            ? location[..2]
            : string.IsNullOrWhiteSpace(location)
                ? string.Empty
                : Path.GetPathRoot(location) ?? string.Empty;
        if (driveRoot.Length >= 2 && driveRoot[1] == ':')
        {
            driveRoot = $"{char.ToUpperInvariant(driveRoot[0])}:\\";
        }

        return new CodexPackageStatus(
            true,
            GetString(root, "Version"),
            GetString(root, "PackageFullName"),
            location,
            driveRoot,
            GetString(root, "Status"));
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
            ? property.ToString()
            : string.Empty;
}
