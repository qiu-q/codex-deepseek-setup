using CodexDeepSeekSetup.Windows.Packages;

namespace CodexDeepSeekSetup.Windows.Tests.Packages;

public sealed class CodexPackageStatusTests
{
    [Fact]
    public void Parse_ExtractsVersionLocationAndDrive()
    {
        const string json = """
            {"Name":"OpenAI.Codex","Version":"26.901.6511.0","PackageFullName":"OpenAI.Codex_26.901.6511.0_x64__2p2nqsd0c76g0","InstallLocation":"D:\\WindowsApps\\OpenAI.Codex_26.901.6511.0_x64__2p2nqsd0c76g0","Status":"Ok"}
            """;

        var status = CodexPackageStatusParser.Parse(json);

        Assert.True(status.IsInstalled);
        Assert.Equal("26.901.6511.0", status.Version);
        Assert.Equal(@"D:\", status.DriveRoot);
        Assert.Equal("Ok", status.Status);
    }

    [Fact]
    public void Parse_EmptyOutputReturnsNotInstalled()
    {
        var status = CodexPackageStatusParser.Parse(string.Empty);

        Assert.False(status.IsInstalled);
        Assert.Equal(string.Empty, status.InstallLocation);
    }
}
