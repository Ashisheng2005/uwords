namespace Uword.App.Input;

public readonly record struct ScreenPoint(int X, int Y);

public enum SelectionGesture { Drag, DoubleClick }

public readonly record struct SelectionCandidate(
    ScreenPoint Position, nint ForegroundWindow, int ProcessId, SelectionGesture Gesture);

public sealed class MouseSelectionClassifier
{
    private const int DragDistanceSquared = 16;
    private const int DoubleClickDistanceSquared = 64;
    private ScreenPoint? _down;
    private ScreenPoint _previousClick;
    private long _previousClickTime;
    private long _downTime;
    private bool _doubleClick;

    public void Press(ScreenPoint point, long time, int doubleClickMilliseconds)
    {
        _doubleClick = _previousClickTime != 0 &&
            time - _previousClickTime <= doubleClickMilliseconds &&
            DistanceSquared(point, _previousClick) <= DoubleClickDistanceSquared;
        _down = point;
        _downTime = time;
    }

    public SelectionGesture? Release(ScreenPoint point)
    {
        if (_down is not ScreenPoint down) return null;
        _down = null;
        if (_doubleClick)
        {
            _previousClickTime = 0;
            return SelectionGesture.DoubleClick;
        }
        if (DistanceSquared(point, down) >= DragDistanceSquared)
        {
            _previousClickTime = 0;
            return SelectionGesture.Drag;
        }
        _previousClick = point;
        _previousClickTime = _downTime;
        return null;
    }

    private static long DistanceSquared(ScreenPoint a, ScreenPoint b)
    {
        long dx = a.X - b.X;
        long dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
