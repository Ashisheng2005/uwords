using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Uword.App.Input;
using Uword.App.Interop;
using Uword.App.Selection;
using Uword.App.Translation;

namespace Uword.App.Overlay;

public sealed class OverlayController : IDisposable
{
    private readonly CircleWindow _circle = new();
    private readonly PreviewWindow _preview = new();
    private ScreenPoint _anchor;
    private NativeMethods.Rect? _recentMarker;
    private long _recentMarkerUntil;

    public event Action? HoverEntered;
    public event Action? HoverLeft;
    public event Action? MarkerClicked;
    public event Action? PreviewDismissed;
    public event Action? SpeakRequested;

    public OverlayController()
    {
        _ = new WindowInteropHelper(_preview).EnsureHandle();
        _circle.MouseEnter += (_, _) => HoverEntered?.Invoke();
        _circle.MouseLeave += (_, _) => HoverLeft?.Invoke();
        _circle.Clicked += () => MarkerClicked?.Invoke();
        _preview.Dismissed += () => PreviewDismissed?.Invoke();
        _preview.SpeakRequested += () => SpeakRequested?.Invoke();
    }

    public void ShowCircle(SelectionSnapshot snapshot)
    {
        _preview.Hide();
        _recentMarker = null;
        var point = snapshot.MousePosition;
        if (snapshot.Bounds is { IsUsable: true } bounds)
        {
            // The pointer is the selection endpoint; keep the marker close to it.
            int x = (int)Math.Round(bounds.Right);
            int y = (int)Math.Round(bounds.Bottom);
            if (Math.Abs(point.X - x) < 80 && Math.Abs(point.Y - y) < 80)
                point = new ScreenPoint(x, y);
        }
        _anchor = new ScreenPoint(point.X + 10, point.Y + 10);
        _circle.Show();
        Place(_circle, _anchor);
    }

    public void ShowPreview(SelectionSnapshot snapshot)
    {
        nint marker = new WindowInteropHelper(_circle).Handle;
        if (NativeMethods.GetWindowRect(marker, out var rect))
        {
            _recentMarker = rect;
            _recentMarkerUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2;
        }
        _circle.Hide();
        string source;
        try { source = Process.GetProcessById(snapshot.ProcessId).ProcessName; }
        catch (Exception) { source = $"Process {snapshot.ProcessId}"; }
        _preview.SetContent(snapshot.Text, source);
        _preview.Show();
        Place(_preview, new ScreenPoint(_anchor.X + 32, _anchor.Y));
    }

    public void SetResult(TranslationResult result) => _preview.SetResult(result);
    public void SetError(string message) => _preview.SetError(message);
    public void SetVoiceStatus(string message) => _preview.SetVoiceStatus(message);

    public bool Contains(ScreenPoint point) => Contains(_circle, point) || Contains(_preview, point) ||
        (_recentMarker is { } rect && Stopwatch.GetTimestamp() <= _recentMarkerUntil && Inside(rect, point));

    public void Hide()
    {
        _recentMarker = null;
        _circle.Hide();
        _preview.Hide();
    }

    private static bool Contains(Window window, ScreenPoint point)
    {
        if (!window.IsVisible) return false;
        nint hwnd = new WindowInteropHelper(window).Handle;
        return NativeMethods.GetWindowRect(hwnd, out var rect) && Inside(rect, point);
    }

    private static bool Inside(NativeMethods.Rect rect, ScreenPoint point) =>
        point.X >= rect.Left && point.X < rect.Right &&
        point.Y >= rect.Top && point.Y < rect.Bottom;

    private static void Place(Window window, ScreenPoint point)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return;
        nint monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.Point { X = point.X, Y = point.Y }, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        int x = Math.Clamp(point.X, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
        int y = Math.Clamp(point.Y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
        NativeMethods.SetWindowPos(hwnd, -1, x, y, 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    public void Dispose()
    {
        _circle.Close();
        _preview.Close();
    }
}
