using System.Windows;
using System.Windows.Interop;
using Uword.App.Interop;

namespace Uword.App.Overlay;

public partial class CircleWindow : Window
{
    public event Action? Clicked;

    public CircleWindow()
    {
        InitializeComponent();
        ToolTip = "单击立即翻译，或悬停一秒";
        System.Windows.Controls.ToolTipService.SetInitialShowDelay(this, 1500);
        MouseLeftButtonUp += (_, _) => Clicked?.Invoke();
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
