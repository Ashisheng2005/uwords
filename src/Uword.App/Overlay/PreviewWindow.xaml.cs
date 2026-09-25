using System.Windows;
using System.Windows.Interop;
using Uword.App.Interop;
using Uword.App.Translation;

namespace Uword.App.Overlay;

public partial class PreviewWindow : Window
{
    public event Action? Dismissed;
    public event Action? SpeakRequested;

    public PreviewWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Dismissed?.Invoke();
        SpeakButton.Click += (_, _) => SpeakRequested?.Invoke();
    }

    public void SetContent(string text, string source)
    {
        SelectedText.Text = text;
        SourceName.Text = source;
        SetVoiceStatus("");
        SetLoading();
    }

    public void SetLoading()
    {
        ResultText.Text = "正在翻译...";
        ResultText.Foreground = System.Windows.Media.Brushes.DimGray;
        ExamplePanel.Visibility = Visibility.Collapsed;
    }

    public void SetError(string message)
    {
        ResultText.Text = message;
        ResultText.Foreground = System.Windows.Media.Brushes.Firebrick;
        ExamplePanel.Visibility = Visibility.Collapsed;
    }

    public void SetResult(TranslationResult result)
    {
        ResultText.Text = result.Translation;
        ResultText.Foreground = System.Windows.Media.Brushes.Black;
        bool hasExample = !string.IsNullOrWhiteSpace(result.ExampleSource) &&
            !string.IsNullOrWhiteSpace(result.ExampleTranslation);
        ExamplePanel.Visibility = hasExample ? Visibility.Visible : Visibility.Collapsed;
        ExampleSource.Text = hasExample ? result.ExampleSource : "";
        ExampleTranslation.Text = hasExample ? result.ExampleTranslation : "";
    }

    public void SetVoiceStatus(string message)
    {
        VoiceStatus.Text = message;
        VoiceStatus.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle,
            style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
    }
}
