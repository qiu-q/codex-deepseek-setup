using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CodexDeepSeekSetup.App.Logic;
using CodexDeepSeekSetup.Core.Results;

namespace CodexDeepSeekSetup.App.Windows.Tests;

public sealed class MainWindowStartupTests
{
    [Fact]
    public void MainWindow_ContainsOnePanelPerGuidedStep()
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application();
                var window = new MainWindow();
                Assert.NotNull(window.FindName("InstallStepPanel"));
                Assert.NotNull(window.FindName("ConfigureStepPanel"));
                Assert.NotNull(window.FindName("CompleteStepPanel"));
                Assert.NotNull(window.FindName("LaunchCodexButton"));
                Assert.NotNull(window.FindName("CloseAssistantButton"));
                var downloadDirectory = Assert.IsType<TextBox>(window.FindName("DownloadDirectoryTextBox"));
                var downloadDirectoryBinding = BindingOperations.GetBinding(
                    downloadDirectory,
                    TextBox.TextProperty);
                Assert.Equal("DownloadDirectory", downloadDirectoryBinding?.Path.Path);
                Assert.Equal(BindingMode.OneWay, downloadDirectoryBinding?.Mode);
                Assert.IsType<ComboBox>(window.FindName("InstallDriveComboBox"));
                Assert.IsType<Button>(window.FindName("BrowseDownloadDirectoryButton"));
                Assert.IsType<Button>(window.FindName("OpenMaintenanceButton"));
                var downloadButton = Assert.IsType<Button>(window.FindName("DownloadCodexButton"));
                var installButton = Assert.IsType<Button>(window.FindName("InstallCodexButton"));
                var fileProgress = Assert.IsType<ProgressBar>(window.FindName("FileProgressBar"));
                var overallProgress = Assert.IsType<ProgressBar>(window.FindName("OverallProgressBar"));
                Assert.IsType<TextBlock>(window.FindName("DownloadPhaseLabel"));
                Assert.IsType<TextBlock>(window.FindName("InstallPhaseLabel"));
                Assert.IsType<ListBox>(window.FindName("ProgressLogList"));
                Assert.Equal("CanDownload", BindingOperations.GetBinding(downloadButton, Button.IsEnabledProperty)?.Path.Path);
                Assert.Equal("CanInstall", BindingOperations.GetBinding(installButton, Button.IsEnabledProperty)?.Path.Path);
                Assert.Equal("FileProgress", BindingOperations.GetBinding(fileProgress, ProgressBar.ValueProperty)?.Path.Path);
                Assert.Equal("OverallProgress", BindingOperations.GetBinding(overallProgress, ProgressBar.ValueProperty)?.Path.Path);
                var cleanupButton = Assert.IsType<Button>(window.FindName("CleanupEverythingButton"));
                var cleanupBinding = BindingOperations.GetBinding(cleanupButton, Button.IsEnabledProperty);
                Assert.Equal("CanCleanup", cleanupBinding?.Path.Path);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(15)), "MainWindow structure check timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void MainWindow_CanBeShownWithoutBindingToAReadOnlyProperty()
    {
        Exception? startupFailure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application();
                var window = new MainWindow();
                window.Show();
                window.Dispatcher.Invoke(() => { });
                window.Close();
            }
            catch (Exception exception)
            {
                startupFailure = exception;
            }
            finally
            {
                completed.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(15)), "MainWindow startup timed out.");
        Assert.Null(startupFailure);
    }

    [Fact]
    public void MaintenanceWindow_ContainsDriveAndSelectiveCleanupControls()
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application();
                var window = new MaintenanceWindow(new FakeMaintenanceActions());
                Assert.IsType<ComboBox>(window.FindName("MaintenanceDriveComboBox"));
                Assert.IsType<ListBox>(window.FindName("ArtifactList"));
                Assert.IsType<Button>(window.FindName("CleanupSelectedButton"));
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(15)), "MaintenanceWindow structure check timed out.");
        Assert.Null(failure);
    }

    private sealed class FakeMaintenanceActions : IMaintenanceActions
    {
        public IReadOnlyList<InstallDriveChoice> InstallDriveChoices { get; } =
            [new(@"D:\", "数据盘 (D:)", true)];

        public string SelectedInstallDrive => @"D:\";

        public Task<OperationResult<IReadOnlyList<MaintenanceArtifactInfo>>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<IReadOnlyList<MaintenanceArtifactInfo>>.Success([]));

        public Task<OperationResult<Unit>> MoveInstalledCodexAsync(string driveRoot, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<Unit>.Success(default));

        public Task<OperationResult<MaintenanceCleanupOutcome>> CleanupSelectedAsync(
            IReadOnlyCollection<string> artifactIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<MaintenanceCleanupOutcome>.Success(new(false)));
    }
}
