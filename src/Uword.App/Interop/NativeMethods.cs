using System.Runtime.InteropServices;
using Uword.App.Input;

namespace Uword.App.Interop;

internal static class NativeMethods
{
    internal const int WhMouseLl = 14;
    internal const int WmLButtonDown = 0x0201;
    internal const int WmLButtonUp = 0x0202;
    internal const int WmQuit = 0x0012;
    internal const int GwlExStyle = -20;
    internal const int WsExNoActivate = 0x08000000;
    internal const int WsExToolWindow = 0x00000080;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint MonitorDefaultToNearest = 2;

    internal delegate nint MouseHookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
        public readonly ScreenPoint ToScreenPoint() => new(X, Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseHookData
    {
        public Point Position;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public nint Hwnd;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Position;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int idHook, MouseHookProc callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    internal static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    internal static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    internal static extern int SetWindowLong(nint hwnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
