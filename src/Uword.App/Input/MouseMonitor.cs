using System.ComponentModel;
using System.Runtime.InteropServices;
using Uword.App.Interop;

namespace Uword.App.Input;

public sealed class MouseMonitor : IDisposable
{
    private readonly MouseSelectionClassifier _classifier = new();
    private readonly NativeMethods.MouseHookProc _callback;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private nint _hook;
    private uint _threadId;
    private Exception? _startupError;

    public event Action<ScreenPoint>? Pressed;
    public event Action<SelectionCandidate>? SelectionCandidateDetected;

    public MouseMonitor() => _callback = OnMouse;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "Uword mouse hook" };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Mouse hook did not start.");
        if (_startupError is not null) throw new InvalidOperationException("Mouse hook installation failed.", _startupError);
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl, _callback, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == 0)
            _startupError = new Win32Exception(Marshal.GetLastWin32Error());
        _ready.Set();
        if (_hook == 0) return;

        try
        {
            while (NativeMethods.GetMessage(out _, 0, 0, 0) > 0) { }
        }
        finally
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            try
            {
                int id = unchecked((int)message);
                if (id is NativeMethods.WmLButtonDown or NativeMethods.WmLButtonUp)
                {
                    var point = Marshal.PtrToStructure<NativeMethods.MouseHookData>(data).Position.ToScreenPoint();
                    if (id == NativeMethods.WmLButtonDown)
                    {
                        _classifier.Press(point, Environment.TickCount64, (int)NativeMethods.GetDoubleClickTime());
                        Pressed?.Invoke(point);
                    }
                    else if (_classifier.Release(point) is SelectionGesture gesture)
                    {
                        nint window = NativeMethods.GetForegroundWindow();
                        NativeMethods.GetWindowThreadProcessId(window, out uint pid);
                        SelectionCandidateDetected?.Invoke(new SelectionCandidate(point, window, (int)pid, gesture));
                    }
                }
            }
            catch (Exception)
            {
                // Exceptions must never escape a global hook callback.
            }
        }
        return NativeMethods.CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (_threadId != 0) NativeMethods.PostThreadMessage(_threadId, NativeMethods.WmQuit, 0, 0);
        _thread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
