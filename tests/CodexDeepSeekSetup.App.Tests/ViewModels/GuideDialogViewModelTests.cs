using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Guides;

namespace CodexDeepSeekSetup.App.Tests.ViewModels;

public sealed class GuideDialogViewModelTests
{
    [Fact]
    public void Navigation_IsBoundedAndLastStepChangesPrimaryLabel()
    {
        var model = new GuideDialogViewModel(Document());

        Assert.Equal(0, model.CurrentIndex);
        Assert.False(model.CanGoPrevious);
        Assert.False(model.IsLastStep);
        Assert.Equal("下一步", model.NextButtonText);

        model.MovePrevious();
        Assert.Equal(0, model.CurrentIndex);
        model.MoveNext();

        Assert.Equal(1, model.CurrentIndex);
        Assert.True(model.CanGoPrevious);
        Assert.True(model.IsLastStep);
        Assert.Equal("完成引导", model.NextButtonText);
        model.MoveNext();
        Assert.Equal(1, model.CurrentIndex);
    }

    private static GuideDocument Document() => new("Guide",
    [
        new GuideStep("one", "One", "Body", "Done", "Open", new Uri("https://example.com/one"), null),
        new GuideStep("two", "Two", "Body", "Done", "Open", new Uri("https://example.com/two"), null)
    ]);
}
