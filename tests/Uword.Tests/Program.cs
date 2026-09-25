using Uword.App.Input;
using Uword.App.Selection;
using Uword.App.Workflow;
using Uword.App.Translation;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

var tests = new (string Name, Action Run)[]
{
    ("ordinary click is ignored", () =>
    {
        var filter = new MouseSelectionClassifier();
        filter.Press(new ScreenPoint(20, 20), 1000, 500);
        Equal(null, filter.Release(new ScreenPoint(21, 20)));
    }),
    ("drag beyond threshold is detected", () =>
    {
        var filter = new MouseSelectionClassifier();
        filter.Press(new ScreenPoint(20, 20), 1000, 500);
        Equal(SelectionGesture.Drag, filter.Release(new ScreenPoint(30, 20)));
    }),
    ("short drag can select a character", () =>
    {
        var filter = new MouseSelectionClassifier();
        filter.Press(new ScreenPoint(20, 20), 1000, 500);
        Equal(SelectionGesture.Drag, filter.Release(new ScreenPoint(24, 20)));
    }),
    ("double click is detected", () =>
    {
        var filter = new MouseSelectionClassifier();
        filter.Press(new ScreenPoint(20, 20), 1000, 500);
        Equal(null, filter.Release(new ScreenPoint(20, 20)));
        filter.Press(new ScreenPoint(21, 21), 1200, 500);
        Equal(SelectionGesture.DoubleClick, filter.Release(new ScreenPoint(21, 21)));
    }),
    ("slow second click is ignored", () =>
    {
        var filter = new MouseSelectionClassifier();
        filter.Press(new ScreenPoint(20, 20), 1000, 500);
        filter.Release(new ScreenPoint(20, 20));
        filter.Press(new ScreenPoint(20, 20), 1600, 500);
        Equal(null, filter.Release(new ScreenPoint(20, 20)));
    }),
    ("drag does not become a double click", () =>
    {
        var filter = new MouseSelectionClassifier();
        filter.Press(new ScreenPoint(-20, 10), 1000, 500);
        Equal(SelectionGesture.Drag, filter.Release(new ScreenPoint(20, 10)));
        filter.Press(new ScreenPoint(20, 10), 1200, 500);
        Equal(null, filter.Release(new ScreenPoint(20, 10)));
    }),
    ("empty release is ignored", () =>
        Equal(null, new MouseSelectionClassifier().Release(new ScreenPoint(0, 0)))),
    ("invalid selection bounds are rejected", () =>
    {
        Equal(false, new ScreenBounds(4, 4, 4, 8).IsUsable);
        Equal(false, new ScreenBounds(double.NaN, 0, 10, 10).IsUsable);
        Equal(true, new ScreenBounds(-10, 0, 10, 10).IsUsable);
    }),
    ("first query gets a separate budget per process", () =>
    {
        var policy = new SelectionProbePolicy();
        Equal(4000, policy.TimeoutMillisecondsFor(10));
        Equal(1500, policy.TimeoutMillisecondsFor(10));
        Equal(4000, policy.TimeoutMillisecondsFor(11));
    }),
    ("empty selection is retried and then succeeds", () =>
    {
        int calls = 0;
        var delays = new List<int>();
        var result = SelectionProbePolicy.ReadWithRetry(() =>
        {
            calls++;
            return calls == 1
                ? SelectionReadResult.Failed(SelectionFailure.EmptySelection)
                : new SelectionReadResult(new SelectionSnapshot("word", 1, 1,
                    new ScreenPoint(0, 0), null, SelectionGesture.Drag), SelectionFailure.None);
        }, delays.Add);
        Equal(2, result.Attempts);
        Equal("word", result.Snapshot?.Text);
        Equal("EmptySelection,None", result.AttemptResults);
        Equal(150, delays.Single());
    }),
    ("retry stops after three attempts", () =>
    {
        int calls = 0;
        var result = SelectionProbePolicy.ReadWithRetry(() =>
        {
            calls++;
            return SelectionReadResult.Failed(SelectionFailure.NoPattern);
        }, _ => { });
        Equal(3, calls);
        Equal(3, result.Attempts);
        Equal("NoPattern,NoPattern,NoPattern", result.AttemptResults);
    }),
    ("source change is not retried", () =>
    {
        int calls = 0;
        var result = SelectionProbePolicy.ReadWithRetry(() =>
        {
            calls++;
            return SelectionReadResult.Failed(SelectionFailure.SourceChanged);
        }, _ => { });
        Equal(1, calls);
        Equal(1, result.Attempts);
    }),
    ("translation defaults and endpoint", () =>
    {
        var settings = new TranslationSettings();
        Equal(0d, settings.Temperature);
        Equal(1200, settings.MaxTextLength);
        Equal(4, settings.MaxParagraphs);
        Equal("https://api.openai.com/v1/chat/completions", settings.Endpoint().ToString());
        Equal("http://localhost:8000/v1/chat/completions", (settings with { BaseUrl = "http://localhost:8000/v1" }).Endpoint().ToString());
        Throws<ArgumentException>(() => (settings with { BaseUrl = "http://example.com/v1" }).Endpoint());
    }),
    ("prompt validation and rendering", () =>
    {
        var settings = new TranslationSettings { Model = "mock", ApiKey = "secret" };
        settings.Validate();
        Equal("Translate to 中文: hi", TranslationSettings.Render("Translate to {{to}}: {{text}}", "中文", "hi"));
        Throws<ArgumentException>(() => (settings with { SingleParagraphPrompt = "{{unknown}} {{text}}" }).Validate());
    }),
    ("paragraph splitting and original breaks", () =>
    {
        var plan = TranslationPlan.Create("A\r\n\r\nB\r\n\r\nC", 1200, 2);
        Equal(2, plan.Batches.Count);
        Equal(2, plan.Batches[0].Pieces.Count);
        Equal("甲\r\n\r\n乙\r\n\r\n丙", plan.Assemble(["甲", "乙", "丙"]));
        Equal("甲,乙", string.Join(',', TranslationPlan.Parse("甲\n\n%%\n\n乙", 2)));
        Throws<FormatException>(() => TranslationPlan.Parse("甲", 2));
    }),
    ("overlong paragraphs are not truncated", () =>
    {
        var plan = TranslationPlan.Create("abcdef", 3, 4);
        Equal(2, plan.Batches.Count);
        Equal("abc def", plan.Assemble(["abc", "def"]));
        Equal(true, plan.Batches.All(b => b.Pieces.Sum(p => p.Text.Length) <= 3));
    }),
    ("literal delimiter is not batched", () =>
    {
        var plan = TranslationPlan.Create("A\n\n%%\n\nB", 1200, 4);
        Equal(true, plan.Batches.All(b => b.Pieces.Count == 1));
    }),
    ("request length includes separator overhead", () =>
    {
        var plan = TranslationPlan.Create("abc\n\ndef", 11, 4);
        Equal(2, plan.Batches.Count);
        var combined = TranslationPlan.Create("abc\n\ndef", 12, 4);
        Equal(1, combined.Batches.Count);
        Equal(12, string.Join("\n\n%%\n\n", combined.Batches[0].Pieces.Select(p => p.Text)).Length);
    })
};

int failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
if (args.Contains("--configured-api"))
{
    try
    {
        var settings = new SettingsStore().Load();
        using var client = new TranslationClient();
        string translated = await client.TestAsync(settings, CancellationToken.None);
        Console.WriteLine($"PASS configured API: received {translated.Length} characters");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL configured API: {ex.GetType().Name}: " +
            (ex is TranslationRequestException or ArgumentException ? ex.Message : "connection or response error"));
    }
}
var asyncTests = new (string Name, Func<Task> Run)[]
{
    ("chat completion request uses configured fields", async () =>
    {
        int calls = 0;
        using var http = new HttpClient(new StubHandler(async (request, token) =>
        {
            calls++;
            Equal("http://localhost:8000/v1/chat/completions", request.RequestUri!.ToString());
            Equal("secret", request.Headers.Authorization!.Parameter);
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Equal("mock", payload.RootElement.GetProperty("model").GetString());
            Equal(0d, payload.RootElement.GetProperty("temperature").GetDouble());
            Equal(false, payload.RootElement.GetProperty("stream").GetBoolean());
            Equal("system", payload.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
            Equal("Translate to 中文: hi", payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
            return Reply("你好");
        }));
        using var client = new TranslationClient(http);
        var settings = new TranslationSettings { BaseUrl = "http://localhost:8000/v1", Model = "mock",
            ApiKey = "secret", TargetLanguage = "中文", SingleParagraphPrompt = "Translate to {{to}}: {{text}}" };
        Equal("你好", await client.TranslateAsync("hi", settings, CancellationToken.None));
        Equal(1, calls);
    }),
    ("malformed multi result falls back to individual requests", async () =>
    {
        int calls = 0;
        using var http = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(Reply(++calls == 1 ? "missing separator" : calls == 2 ? "甲" : "乙"))));
        using var client = new TranslationClient(http);
        var settings = new TranslationSettings { BaseUrl = "http://localhost:8000/v1", Model = "mock", ApiKey = "secret" };
        Equal("甲\n\n乙", await client.TranslateAsync("A\n\nB", settings, CancellationToken.None));
        Equal(3, calls);
    }),
    ("http errors do not leak response bodies", async () =>
    {
        using var http = new HttpClient(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("sensitive body") })));
        using var client = new TranslationClient(http);
        var settings = new TranslationSettings { BaseUrl = "http://localhost:8000/v1", Model = "mock", ApiKey = "secret" };
        try { await client.TranslateAsync("hello", settings, CancellationToken.None); throw new Exception("Expected failure"); }
        catch (TranslationRequestException ex)
        {
            Equal(401, ex.StatusCode);
            Equal(false, ex.Message.Contains("sensitive body"));
        }
    }),
    ("rate limit includes retry and connection test", async () =>
    {
        int calls = 0;
        var times = new List<DateTimeOffset>();
        using var http = new HttpClient(new StubHandler((_, _) =>
        {
            times.Add(DateTimeOffset.UtcNow);
            var response = ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Reply("好");
            if (calls == 1) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }));
        using var client = new TranslationClient(http);
        var settings = new TranslationSettings { BaseUrl = "http://localhost:8000/v1", Model = "mock", ApiKey = "secret",
            RequestsPerSecond = 2 };
        Equal("好", await client.TestAsync(settings, CancellationToken.None));
        Equal("好", await client.TranslateAsync("hello", settings, CancellationToken.None));
        Equal(3, calls);
        Equal(true, (times[1] - times[0]).TotalMilliseconds >= 450);
        Equal(true, (times[2] - times[1]).TotalMilliseconds >= 450);
    }),
    ("new selection cancellation stops pending request", async () =>
    {
        int calls = 0;
        using var http = new HttpClient(new StubHandler(async (_, token) =>
        {
            calls++;
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return Reply("unreachable");
        }));
        using var client = new TranslationClient(http);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var settings = new TranslationSettings { BaseUrl = "http://localhost:8000/v1", Model = "mock", ApiKey = "secret" };
        try { await client.TranslateAsync("hello", settings, cancel.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { Equal(1, calls); }
    })
};
foreach (var (name, run) in asyncTests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
if (args.Contains("--integration"))
{
    try
    {
        bool uiaVerified = await RunUiaIntegrationAsync(args);
        Console.WriteLine(uiaVerified ? "PASS UI Automation selected text" :
            "SKIP UI Automation selected text (test window could not become foreground)");
        using (var monitor = new MouseMonitor())
        {
            monitor.Start();
            Console.WriteLine("PASS low-level mouse hook startup");
        }
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL UI Automation selected text: {ex}");
    }
}
return failures == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

static HttpResponseMessage Reply(string content) => new(HttpStatusCode.OK)
{
    Content = new StringContent(JsonSerializer.Serialize(new
    {
        choices = new[] { new { finish_reason = "stop", message = new { content } } }
    }))
};

static async Task<bool> RunUiaIntegrationAsync(string[] args)
{
    var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        var box = new TextBox { Text = "Uword selection test", FontSize = 18 };
        var window = new Window { Title = "Uword UIA test", Width = 340, Height = 140, Content = box, Topmost = true };
        window.Closed += (_, _) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        window.Loaded += async (_, _) =>
        {
            try
            {
                window.Activate();
                box.Focus();
                box.Select(0, 5);
                await Task.Delay(300);
                var point = box.PointToScreen(new System.Windows.Point(20, 20));
                var candidate = new SelectionCandidate(
                    new ScreenPoint((int)point.X, (int)point.Y),
                    new WindowInteropHelper(window).Handle, Environment.ProcessId, SelectionGesture.Drag);
                int hostArg = Array.IndexOf(args, "--host");
                string host = hostArg >= 0 && hostArg + 1 < args.Length
                    ? args[hostArg + 1]
                    : System.IO.Path.Combine(AppContext.BaseDirectory, "Uword.App.exe");
                var reader = new SelectionClient(host);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                SelectionReadResult result = SelectionReadResult.Failed(SelectionFailure.SourceChanged);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    window.Activate();
                    box.Focus();
                    box.Select(0, 5);
                    await Task.Delay(150);
                    result = await reader.ReadAsync(candidate, CancellationToken.None);
                    if (result.Failure != SelectionFailure.SourceChanged) break;
                }
                Console.WriteLine($"UIA query: {watch.ElapsedMilliseconds} ms");
                bool foregroundUnavailable = result.Failure == SelectionFailure.SourceChanged &&
                    ForegroundProbe.GetForegroundWindow() != new WindowInteropHelper(window).Handle;
                using (var client = new TranslationClient())
                {
                    var settingsWindow = new SettingsWindow(new SettingsStore(), client, new TranslationSettings());
                    settingsWindow.Show();
                    Equal(true, settingsWindow.ActualWidth >= 540);
                    Equal(true, settingsWindow.FindName("SaveButton") is Button);
                    settingsWindow.Hide();
                    Console.WriteLine("PASS translation settings window startup");
                }
                completion.TrySetResult(foregroundUnavailable ? "ForegroundUnavailable" :
                    result.Snapshot?.Text ?? result.Failure.ToString());
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { window.Close(); }
        };
        window.Show();
        Dispatcher.Run();
    }) { IsBackground = true };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    string text = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
    thread.Join(TimeSpan.FromSeconds(2));
    if (text == "ForegroundUnavailable") return false;
    Equal("Uword", text);
    return true;
}

sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => handler(request, token);
}

static class ForegroundProbe
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();
}
