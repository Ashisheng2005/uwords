using System.Diagnostics;
using System.Text.Json;
using Uword.App.Input;

namespace Uword.App.Selection;

public sealed class SelectionClient
{
    private readonly string _hostPath;

    public SelectionClient(string hostPath) => _hostPath = hostPath;

    public async Task<SelectionReadResult> ReadAsync(SelectionCandidate candidate, CancellationToken cancellation)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(_hostPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            }
        };
        process.StartInfo.ArgumentList.Add(SelectionHost.Argument);

        try
        {
            process.Start();
            var query = new SelectionQueryMessage(candidate.Position.X, candidate.Position.Y,
                (long)candidate.ForegroundWindow, candidate.ProcessId, candidate.Gesture);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(query));
            process.StandardInput.Close();

            string? line = await process.StandardOutput.ReadLineAsync(cancellation);
            if (line is null) return SelectionReadResult.Failed(SelectionFailure.Error);
            var reply = JsonSerializer.Deserialize<SelectionReplyMessage>(line);
            if (reply is null || reply.Failure != SelectionFailure.None || reply.Text is null)
                return new SelectionReadResult(null, reply?.Failure ?? SelectionFailure.Error,
                    reply?.Attempts ?? 1, reply?.AttemptResults);

            return new SelectionReadResult(new SelectionSnapshot(reply.Text, reply.ProcessId,
                (nint)reply.Window, new ScreenPoint(reply.X, reply.Y), reply.Bounds,
                reply.Gesture), SelectionFailure.None, reply.Attempts, reply.AttemptResults);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return SelectionReadResult.Failed(SelectionFailure.Error); }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }
}
