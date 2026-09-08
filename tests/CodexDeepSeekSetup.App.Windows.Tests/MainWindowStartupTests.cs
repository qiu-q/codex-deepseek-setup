using System.Windows;

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
                Assert.NotNull(window.FindName("CleanupEverythingButton"));
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
}
