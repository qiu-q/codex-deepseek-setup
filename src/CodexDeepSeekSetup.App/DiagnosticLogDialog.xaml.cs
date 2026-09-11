using System.Windows;
using CodexDeepSeekSetup.App.Logic.Diagnostics;

namespace CodexDeepSeekSetup.App;

public partial class DiagnosticLogDialog : Window
{
    private readonly DiagnosticLog diagnosticLog;

    public DiagnosticLogDialog(DiagnosticLog diagnosticLog)
    {
        ArgumentNullException.ThrowIfNull(diagnosticLog);
        InitializeComponent();
        this.diagnosticLog = diagnosticLog;
        DiagnosticLogPathText.Text = diagnosticLog.FilePath;
        RefreshLog();
    }

    private void Window_Activated(object? sender, EventArgs e) => RefreshLog();

    private void CopyAllDiagnosticLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(diagnosticLog.Snapshot());
            DiagnosticLogActionStatus.Text = "日志已复制，可以直接粘贴发送。";
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            DiagnosticLogActionStatus.Text = "剪贴板暂时不可用，请点击“导出到桌面”。";
            diagnosticLog.RecordException("diagnostics-copy", error);
        }
    }

    private void ExportDiagnosticLogButton_Click(object sender, RoutedEventArgs e)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var result = diagnosticLog.ExportTo(desktop);
        diagnosticLog.RecordResult("diagnostics-export", result);
        DiagnosticLogActionStatus.Text = result.IsSuccess
            ? $"已导出：{result.Value}"
            : result.ErrorMessage ?? "导出失败";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void RefreshLog()
    {
        DiagnosticLogTextBox.Text = diagnosticLog.Snapshot();
        DiagnosticLogTextBox.ScrollToEnd();
    }
}
