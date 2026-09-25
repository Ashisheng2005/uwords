using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using Uword.App.Input;
using Uword.App.Interop;

namespace Uword.App.Selection;

public sealed class UiaSelectionReader : IDisposable
{
    private const int MaxTextLength = 4000;
    private readonly BlockingCollection<Query> _queue = new();
    private readonly Thread _worker;

    private sealed record Query(SelectionCandidate Candidate, TaskCompletionSource<SelectionReadResult> Completion);

    public UiaSelectionReader()
    {
        _worker = new Thread(Run) { IsBackground = true, Name = "Uword UI Automation" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    public Task<SelectionReadResult> ReadAsync(SelectionCandidate candidate, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<SelectionReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        while (_queue.TryTake(out var stale))
            stale.Completion.TrySetResult(SelectionReadResult.Failed(SelectionFailure.Superseded));

        if (cancellation.IsCancellationRequested || _queue.IsAddingCompleted)
        {
            completion.TrySetCanceled(cancellation);
            return completion.Task;
        }

        cancellation.Register(() => completion.TrySetCanceled(cancellation));
        try { _queue.Add(new Query(candidate, completion)); }
        catch (InvalidOperationException) { completion.TrySetCanceled(); }
        return completion.Task;
    }

    private void Run()
    {
        foreach (var query in _queue.GetConsumingEnumerable())
        {
            if (query.Completion.Task.IsCompleted) continue;
            try { query.Completion.TrySetResult(Read(query.Candidate)); }
            catch (Exception) { query.Completion.TrySetResult(SelectionReadResult.Failed(SelectionFailure.Error)); }
        }
    }

    private static SelectionReadResult Read(SelectionCandidate candidate)
    {
        if (candidate.ForegroundWindow == 0 ||
            NativeMethods.GetForegroundWindow() != candidate.ForegroundWindow)
            return SelectionReadResult.Failed(SelectionFailure.SourceChanged);

        bool foundPattern = false;
        IUIAutomation automation = new CUIAutomation();
        foreach (var element in Candidates(automation, candidate.Position))
        {
            try
            {
                if (element.CurrentProcessId != candidate.ProcessId) continue;
                if (element.CurrentIsPassword != 0)
                    return SelectionReadResult.Failed(SelectionFailure.ProtectedField);
                if (element.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId) is not IUIAutomationTextPattern pattern)
                    continue;
                foundPattern = true;

                var ranges = pattern.GetSelection();
                for (int i = 0; i < ranges.Length; i++)
                {
                    var range = ranges.GetElement(i);
                    string text = range.GetText(MaxTextLength).Trim();
                    if (text.Length == 0) continue;

                    ScreenBounds? bounds = null;
                    try
                    {
                        double[] rectangles = range.GetBoundingRectangles();
                        for (int offset = 0; offset + 3 < rectangles.Length; offset += 4)
                        {
                            var value = new ScreenBounds(rectangles[offset], rectangles[offset + 1],
                                rectangles[offset] + rectangles[offset + 2],
                                rectangles[offset + 1] + rectangles[offset + 3]);
                            if (value.IsUsable) bounds = value;
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or COMException) { }

                    return new SelectionReadResult(new SelectionSnapshot(
                        text, candidate.ProcessId, candidate.ForegroundWindow,
                        candidate.Position, bounds, candidate.Gesture), SelectionFailure.None);
                }
            }
            catch (COMException) { }
            catch (InvalidOperationException) { }
        }

        return SelectionReadResult.Failed(foundPattern ? SelectionFailure.EmptySelection : SelectionFailure.NoPattern);
    }

    private static IEnumerable<IUIAutomationElement> Candidates(IUIAutomation automation, ScreenPoint position)
    {
        IUIAutomationElement? focused = null;
        IUIAutomationElement? underMouse = null;
        try { focused = automation.GetFocusedElement(); }
        catch (COMException) { }
        try { underMouse = automation.ElementFromPoint(new tagPOINT { x = position.X, y = position.Y }); }
        catch (COMException) { }

        foreach (var root in new[] { focused, underMouse })
        {
            var current = root;
            for (int depth = 0; current is not null && depth < 4; depth++)
            {
                yield return current;
                IUIAutomationElement? parent = null;
                try { parent = automation.ControlViewWalker.GetParentElement(current); }
                catch (COMException) { }
                current = parent;
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        while (_queue.TryTake(out var pending)) pending.Completion.TrySetCanceled();
        if (_worker.Join(TimeSpan.FromMilliseconds(200))) _queue.Dispose();
    }
}
