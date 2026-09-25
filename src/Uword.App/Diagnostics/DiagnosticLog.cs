using System.Diagnostics;
using System.IO;
using Uword.App.Input;
using Uword.App.Selection;

namespace Uword.App.Diagnostics;

public sealed class DiagnosticLog
{
    private const long MaxBytes = 1_000_000;
    private readonly object _gate = new();

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Uword", "diagnostics.log");

    public void WriteEvent(string name) => Write($"event={name}");

    public void WriteProbe(SelectionCandidate candidate, SelectionFailure failure,
        int attempts, long elapsedMilliseconds, int characters, int timeoutMilliseconds,
        string? attemptResults = null)
    {
        string processName;
        try { processName = Process.GetProcessById(candidate.ProcessId).ProcessName.Replace('\r', '_').Replace('\n', '_'); }
        catch (Exception) { processName = "unknown"; }
        Write($"process={processName} pid={candidate.ProcessId} gesture={candidate.Gesture} " +
            $"result={failure} attempts={attempts} elapsed_ms={elapsedMilliseconds} " +
            $"characters={characters} timeout_ms={timeoutMilliseconds} " +
            $"attempt_results={attemptResults ?? "-"}");
    }

    private void Write(string message)
    {
        try
        {
            lock (_gate)
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    File.WriteAllText(path, string.Empty);
                File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
