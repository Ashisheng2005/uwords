using Uword.App.Selection;

namespace Uword.App.Workflow;

public sealed class SelectionProbePolicy
{
    private readonly HashSet<int> _seenProcesses = new();

    public int TimeoutMillisecondsFor(int processId) => _seenProcesses.Add(processId) ? 4000 : 1500;

    public static bool ShouldRetry(SelectionFailure failure, int attempt) =>
        attempt < 3 && failure is SelectionFailure.NoPattern or SelectionFailure.EmptySelection;

    public static int RetryDelayMilliseconds(int attempt) => attempt == 1 ? 150 : 300;

    public static SelectionReadResult ReadWithRetry(Func<SelectionReadResult> read, Action<int> delay)
    {
        var outcomes = new List<SelectionFailure>();
        for (int attempt = 1; ; attempt++)
        {
            SelectionReadResult result = read();
            outcomes.Add(result.Failure);
            result = result with { Attempts = attempt, AttemptResults = string.Join(",", outcomes) };
            if (!ShouldRetry(result.Failure, attempt)) return result;
            delay(RetryDelayMilliseconds(attempt));
        }
    }
}
