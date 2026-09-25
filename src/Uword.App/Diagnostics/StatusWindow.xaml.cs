using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace Uword.App.Diagnostics;

public partial class StatusWindow : Window
{
    public StatusWindow()
    {
        InitializeComponent();
        OpenLogButton.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe")
                {
                    UseShellExecute = false,
                    ArgumentList = { DiagnosticLog.FilePath }
                });
            }
            catch (Exception ex) { SetStatus($"Cannot open log: {ex.Message}"); }
        };
    }

    public void SetStatus(string status) => StatusText.Text = status;

    public void ShowStatus()
    {
        Show();
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
