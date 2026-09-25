using System.Text.Json;
using Uword.App.Input;
using Uword.App.Workflow;

namespace Uword.App.Selection;

internal sealed record SelectionQueryMessage(int X, int Y, long Window, int ProcessId, SelectionGesture Gesture);
internal sealed record SelectionReplyMessage(string? Text, int ProcessId, long Window,
    int X, int Y, ScreenBounds? Bounds, SelectionGesture Gesture, SelectionFailure Failure,
    int Attempts, string? AttemptResults);

internal static class SelectionHost
{
    public const string Argument = "--selection-host";

    public static int Run()
    {
        try
        {
            string? line = Console.ReadLine();
            if (line is null) return 1;
            var request = JsonSerializer.Deserialize<SelectionQueryMessage>(line);
            if (request is null) return 1;

            var candidate = new SelectionCandidate(
                new ScreenPoint(request.X, request.Y), (nint)request.Window,
                request.ProcessId, request.Gesture);
            using var reader = new UiaSelectionReader();
            SelectionReadResult result = SelectionProbePolicy.ReadWithRetry(
                () => reader.ReadAsync(candidate, CancellationToken.None).GetAwaiter().GetResult(), Thread.Sleep);
            var snapshot = result.Snapshot;
            var reply = new SelectionReplyMessage(snapshot?.Text, request.ProcessId, request.Window,
                request.X, request.Y, snapshot?.Bounds, request.Gesture, result.Failure,
                result.Attempts, result.AttemptResults);
            Console.WriteLine(JsonSerializer.Serialize(reply));
            Console.Out.Flush();
            return 0;
        }
        catch (Exception)
        {
            Console.WriteLine(JsonSerializer.Serialize(new SelectionReplyMessage(
                null, 0, 0, 0, 0, null, SelectionGesture.Drag, SelectionFailure.Error, 1, "Error")));
            Console.Out.Flush();
            return 1;
        }
    }
}
