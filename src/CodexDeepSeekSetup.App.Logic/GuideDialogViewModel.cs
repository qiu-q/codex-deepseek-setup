using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexDeepSeekSetup.Core.Guides;

namespace CodexDeepSeekSetup.App.Logic;

public sealed class GuideDialogViewModel : INotifyPropertyChanged
{
    private int currentIndex;

    public GuideDialogViewModel(GuideDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Steps.Count == 0)
        {
            throw new ArgumentException("引导至少需要一个步骤。", nameof(document));
        }
        Document = document;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GuideDocument Document { get; }
    public int CurrentIndex => currentIndex;
    public GuideStep CurrentStep => Document.Steps[currentIndex];
    public string PositionText => $"{currentIndex + 1} / {Document.Steps.Count}";
    public bool CanGoPrevious => currentIndex > 0;
    public bool IsLastStep => currentIndex == Document.Steps.Count - 1;
    public string NextButtonText => IsLastStep ? "完成引导" : "下一步";

    public void MovePrevious() => MoveTo(currentIndex - 1);
    public void MoveNext() => MoveTo(currentIndex + 1);

    private void MoveTo(int index)
    {
        var next = Math.Clamp(index, 0, Document.Steps.Count - 1);
        if (next == currentIndex)
        {
            return;
        }
        currentIndex = next;
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(NextButtonText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
