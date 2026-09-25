using System.Windows;
using System.Windows.Interop;
using Uword.App.Interop;

namespace Uword.App.Overlay;

public partial class PreviewWindow : Window
{
    public event Action? Dismissed;

    public PreviewWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Dismissed?.Invoke();
    }

    public void SetContent(string text, string source)
    {
        SelectedText.Text = text;
        SourceName.Text = source;
        TranslatedText.Text = "翻译中...";
    }

    public void SetTranslation(string text) => TranslatedText.Text = text;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle,
            style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
    }
}
