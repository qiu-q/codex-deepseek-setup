using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Guides;

namespace CodexDeepSeekSetup.App.Tests.ViewModels;

public sealed class DeepSeekGuideCatalogTests
{
    [Fact]
    public void Document_ProvidesFourOrderedOfficialDeepSeekActions()
    {
        var document = DeepSeekGuideCatalog.Document;
        var items = document.Steps;

        Assert.Equal(["sign-in", "identity", "top-up", "api-key"], items.Select(item => item.Id));
        Assert.Equal(4, items.Count);
        Assert.Contains(items, item => item.ActionUrl.AbsolutePath == "/sign_in");
        Assert.Contains(items, item => item.ActionUrl.AbsolutePath == "/top_up");
        Assert.Contains(items, item => item.ActionUrl.AbsolutePath == "/api_keys");
        Assert.All(items, item =>
        {
            Assert.Equal(Uri.UriSchemeHttps, item.ActionUrl.Scheme);
            Assert.Equal("platform.deepseek.com", item.ActionUrl.Host);
            Assert.False(string.IsNullOrWhiteSpace(item.ActionText));
            Assert.False(string.IsNullOrWhiteSpace(item.CompletionHint));
        });
    }
}
