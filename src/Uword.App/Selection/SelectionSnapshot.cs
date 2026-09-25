using Uword.App.Input;

namespace Uword.App.Selection;

public readonly record struct ScreenBounds(double Left, double Top, double Right, double Bottom)
{
    public bool IsUsable => Right > Left && Bottom > Top &&
        double.IsFinite(Left) && double.IsFinite(Top) &&
        double.IsFinite(Right) && double.IsFinite(Bottom);
}

public sealed record SelectionSnapshot(
    string Text, int ProcessId, nint ForegroundWindow,
    ScreenPoint MousePosition, ScreenBounds? Bounds, SelectionGesture Gesture);

public enum SelectionFailure
{
    None, NoPattern, EmptySelection, ProtectedField, SourceChanged, Error, Superseded, Timeout
}

public sealed record SelectionReadResult(
    SelectionSnapshot? Snapshot, SelectionFailure Failure, int Attempts = 1, string? AttemptResults = null)
{
    public static SelectionReadResult Failed(SelectionFailure reason) => new(null, reason);
}
