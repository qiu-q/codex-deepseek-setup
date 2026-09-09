using CodexDeepSeekSetup.App.Logic;

namespace CodexDeepSeekSetup.App.Tests.ViewModels;

public sealed class DeepSeekGuideCatalogTests
{
    [Fact]
    public void Items_ProvideFourOrderedOfficialDeepSeekActions()
    {
        var items = DeepSeekGuideCatalog.Items;

        Assert.Equal([1, 2, 3, 4], items.Select(item => item.Number));
        Assert.Equal(4, items.Count);
        Assert.Contains(items, item => item.Url.AbsolutePath == "/sign_in");
        Assert.Contains(items, item => item.Url.AbsolutePath == "/top_up");
        Assert.Contains(items, item => item.Url.AbsolutePath == "/api_keys");
        Assert.All(items, item =>
        {
            Assert.Equal(Uri.UriSchemeHttps, item.Url.Scheme);
            Assert.Equal("platform.deepseek.com", item.Url.Host);
            Assert.False(string.IsNullOrWhiteSpace(item.ActionLabel));
        });
    }
}
