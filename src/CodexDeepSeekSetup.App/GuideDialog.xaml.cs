using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Advertisements;
using CodexDeepSeekSetup.Core.Guides;

namespace CodexDeepSeekSetup.App;

public partial class GuideDialog : Window
{
    private readonly GuideDialogViewModel viewModel;
    private readonly AdvertisementImageDownloader? imageDownloader;
    private readonly CancellationTokenSource lifetime = new();

    public GuideDialog(GuideDocument document, AdvertisementImageDownloader? imageDownloader)
    {
        InitializeComponent();
        viewModel = new GuideDialogViewModel(document);
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        this.imageDownloader = imageDownloader;
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        lifetime.Cancel();
        lifetime.Dispose();
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        base.OnClosed(e);
    }

    private async void GuideDialog_ContentRendered(object? sender, EventArgs e) => await LoadCurrentImageAsync();
    private void DismissGuideButton_Click(object sender, RoutedEventArgs e) => Close();
    private void PreviousGuideButton_Click(object sender, RoutedEventArgs e) => viewModel.MovePrevious();

    private void NextGuideButton_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsLastStep)
        {
            DialogResult = true;
            return;
        }
        viewModel.MoveNext();
    }

    private void OpenGuideActionButton_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(viewModel.CurrentStep.ActionUrl.AbsoluteUri) { UseShellExecute = true });

    private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GuideDialogViewModel.CurrentStep))
        {
            await LoadCurrentImageAsync();
        }
    }

    private async Task LoadCurrentImageAsync()
    {
        GuideImage.Source = null;
        GuideImage.Visibility = Visibility.Collapsed;
        GuideImageFallback.Visibility = Visibility.Visible;
        var uri = viewModel.CurrentStep.ImageUrl;
        if (uri is null || imageDownloader is null)
        {
            return;
        }

        var bytes = await imageDownloader.DownloadAsync(uri, lifetime.Token);
        if (bytes is null || lifetime.IsCancellationRequested || uri != viewModel.CurrentStep.ImageUrl)
        {
            return;
        }
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        GuideImage.Source = bitmap;
        GuideImage.Visibility = Visibility.Visible;
        GuideImageFallback.Visibility = Visibility.Collapsed;
    }
}
