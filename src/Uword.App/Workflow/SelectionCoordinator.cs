using System.Diagnostics;
using System.Net.Http;
using System.Windows.Threading;
using Uword.App.Audio;
using Uword.App.Diagnostics;
using Uword.App.Input;
using Uword.App.Interop;
using Uword.App.Overlay;
using Uword.App.Selection;
using Uword.App.Translation;

namespace Uword.App.Workflow;

public sealed class SelectionCoordinator : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly MouseMonitor _mouse;
    private readonly SelectionClient _reader;
    private readonly OverlayController _overlay;
    private readonly StatusWindow _diagnostics;
    private readonly DiagnosticLog _log;
    private readonly TranslationClient _translator;
    private readonly PronunciationPlayer _audio = new();
    private TranslationSettings _settings;
    private readonly SelectionProbePolicy _policy = new();
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _sourceTimer;
    private CancellationTokenSource? _probe;
    private CancellationTokenSource? _translation;
    private SelectionSnapshot? _current;
    private int _generation;
    private int _sourceChangeTicks;
    private int _requestVersion;
    private bool _triggered;
    private bool _enabled = true;

    public SelectionCoordinator(Dispatcher dispatcher, MouseMonitor mouse, SelectionClient reader,
        OverlayController overlay, StatusWindow diagnostics, DiagnosticLog log,
        TranslationClient translator, TranslationSettings settings)
    {
        _dispatcher = dispatcher;
        _mouse = mouse;
        _reader = reader;
        _overlay = overlay;
        _diagnostics = diagnostics;
        _log = log;
        _translator = translator;
        _settings = settings;
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sourceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

        _mouse.Pressed += OnPressed;
        _mouse.SelectionCandidateDetected += OnCandidate;
        _overlay.HoverEntered += OnHoverEntered;
        _overlay.HoverLeft += OnHoverLeft;
        _overlay.MarkerClicked += OnMarkerClicked;
        _overlay.SpeakRequested += OnSpeakRequested;
        _overlay.PreviewDismissed += Reset;
        _hoverTimer.Tick += OnHoverElapsed;
        _sourceTimer.Tick += OnSourceTick;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            Reset();
            _diagnostics.SetStatus(value ? "Ready. Select text in another application." : "Selection detection paused.");
        }
    }

    public void UpdateSettings(TranslationSettings settings)
    {
        _settings = settings;
        Reset();
        _diagnostics.SetStatus("翻译设置已更新，等待选词。");
    }

    private void OnPressed(ScreenPoint point) => _dispatcher.BeginInvoke(() =>
    {
        if (!_overlay.Contains(point))
        {
            if (_current is not null) _log.WriteEvent("marker_dismissed_by_click");
            Reset();
        }
    });

    private void OnCandidate(SelectionCandidate candidate) => _dispatcher.BeginInvoke(() =>
    {
        if (!_enabled || candidate.ProcessId == Environment.ProcessId || _overlay.Contains(candidate.Position)) return;
        _ = ProbeAsync(candidate);
    });

    private async Task ProbeAsync(SelectionCandidate candidate)
    {
        Reset();
        int generation = _generation;
        _probe = new CancellationTokenSource();
        CancellationToken token = _probe.Token;
        _diagnostics.SetStatus($"Checking {candidate.Gesture} selection in process {candidate.ProcessId}...");
        var stopwatch = new Stopwatch();
        int timeoutMilliseconds = 0;

        try
        {
            await Task.Delay(80, token);
            timeoutMilliseconds = _policy.TimeoutMillisecondsFor(candidate.ProcessId);
            stopwatch.Start();
            Task<SelectionReadResult> read = _reader.ReadAsync(candidate, token);
            Task timeout = Task.Delay(timeoutMilliseconds, token);
            if (await Task.WhenAny(read, timeout) != read)
            {
                if (generation == _generation && !token.IsCancellationRequested)
                {
                    _log.WriteProbe(candidate, SelectionFailure.Timeout, 0, stopwatch.ElapsedMilliseconds, 0, timeoutMilliseconds);
                    _diagnostics.SetStatus($"Selection query timed out after {stopwatch.ElapsedMilliseconds} ms (process {candidate.ProcessId}). See diagnostics log.");
                    _probe.Cancel();
                }
                return;
            }

            SelectionReadResult result = await read;
            if (generation != _generation || token.IsCancellationRequested) return;
            _log.WriteProbe(candidate, result.Failure, result.Attempts, stopwatch.ElapsedMilliseconds,
                result.Snapshot?.Text.Length ?? 0, timeoutMilliseconds, result.AttemptResults);
            if (result.Snapshot is null)
            {
                _diagnostics.SetStatus($"No selection: {result.Failure} after {result.Attempts} attempt(s), {stopwatch.ElapsedMilliseconds} ms (process {candidate.ProcessId}).");
                return;
            }

            _current = result.Snapshot;
            _overlay.ShowCircle(_current);
            _sourceTimer.Start();
            _log.WriteEvent("marker_shown");
            _diagnostics.SetStatus($"Selected {_current.Text.Length} characters in process {_current.ProcessId} ({result.Attempts} attempt(s), {stopwatch.ElapsedMilliseconds} ms). Hover over the marker.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                _log.WriteProbe(candidate, SelectionFailure.Error, 0, stopwatch.ElapsedMilliseconds, 0, timeoutMilliseconds);
                _diagnostics.SetStatus($"Selection error: {ex.GetType().Name}.");
            }
        }
    }

    private void OnHoverEntered()
    {
        if (_current is null || _triggered) return;
        _hoverTimer.Start();
        _log.WriteEvent("marker_hover_started");
        _diagnostics.SetStatus("已进入标志，保持悬停一秒以翻译。");
    }

    private void OnHoverLeft()
    {
        if (_hoverTimer.IsEnabled) _log.WriteEvent("marker_hover_interrupted");
        _hoverTimer.Stop();
    }

    private void OnHoverElapsed(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        Trigger("hover");
    }

    private void OnMarkerClicked() => Trigger("click");

    private void Trigger(string source)
    {
        if (_current is null || _triggered) return;
        _triggered = true;
        _hoverTimer.Stop();
        var elapsed = Stopwatch.StartNew();
        _overlay.ShowPreview(_current);
        _log.WriteEvent($"preview_shown trigger={source} elapsed_ms={elapsed.ElapsedMilliseconds}");
        _ = TranslateAsync();
    }

    private void OnSpeakRequested()
    {
        if (!_triggered || _current is null) return;
        _overlay.SetVoiceStatus(_audio.TrySpeak(_current.Text.Trim(), out string error) ? "" : error);
    }

    private async Task TranslateAsync()
    {
        if (_current is null) return;
        int generation = _generation;
        var snapshot = _current;
        if (string.IsNullOrWhiteSpace(_settings.Model) || string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _log.WriteEvent("translation_not_configured");
            _overlay.SetError("请在托盘菜单中配置翻译接口。");
            _diagnostics.SetStatus("请先打开翻译设置，填写模型和 API Key。");
            return;
        }
        _translation?.Cancel();
        var request = new CancellationTokenSource();
        _translation = request;
        int version = ++_requestVersion;
        _log.WriteEvent("translation_started");
        _diagnostics.SetStatus("正在请求翻译...");
        try
        {
            TranslationResult result = await _translator.TranslateResultAsync(snapshot.Text, _settings, request.Token);
            if (generation != _generation || version != _requestVersion) return;
            _overlay.SetResult(result);
            _log.WriteEvent("translation_completed");
            _diagnostics.SetStatus($"已处理 {snapshot.Text.Length} 个字符。");
        }
        catch (OperationCanceledException)
        {
            if (generation != _generation || version != _requestVersion || request.IsCancellationRequested) return;
            _log.WriteEvent("translation_timeout");
            _overlay.SetError("翻译请求超时。");
            _diagnostics.SetStatus("翻译请求超时。");
        }
        catch (Exception ex) when (ex is ArgumentException or TranslationRequestException or
            HttpRequestException or FormatException or System.Text.Json.JsonException)
        {
            if (generation != _generation || version != _requestVersion) return;
            _log.WriteEvent($"translation_failed_{ex.GetType().Name}");
            string message = ex is HttpRequestException ? "网络连接失败。" :
                ex is System.Text.Json.JsonException ? "翻译服务响应格式错误。" : ex.Message;
            _overlay.SetError(message);
            _diagnostics.SetStatus($"翻译失败：{message}");
        }
        finally
        {
            if (ReferenceEquals(_translation, request)) _translation = null;
            request.Dispose();
        }
    }

    private void OnSourceTick(object? sender, EventArgs e)
    {
        if (_current is null) return;
        if (NativeMethods.GetForegroundWindow() == _current.ForegroundWindow)
        {
            _sourceChangeTicks = 0;
            return;
        }
        if (++_sourceChangeTicks >= 2)
        {
            _log.WriteEvent("source_foreground_changed");
            Reset();
            _diagnostics.SetStatus("选词所在窗口已失去焦点，标志已关闭。");
        }
    }

    private void Reset()
    {
        ++_generation;
        ++_requestVersion;
        if (_translation is not null) _log.WriteEvent("translation_session_ended");
        _probe?.Cancel();
        _probe?.Dispose();
        _probe = null;
        _translation?.Cancel();
        _translation = null;
        _audio.Stop();
        _hoverTimer.Stop();
        _sourceTimer.Stop();
        _current = null;
        _sourceChangeTicks = 0;
        _triggered = false;
        _overlay.Hide();
    }

    public void Dispose()
    {
        Reset();
        _mouse.Pressed -= OnPressed;
        _mouse.SelectionCandidateDetected -= OnCandidate;
        _overlay.HoverEntered -= OnHoverEntered;
        _overlay.HoverLeft -= OnHoverLeft;
        _overlay.MarkerClicked -= OnMarkerClicked;
        _overlay.SpeakRequested -= OnSpeakRequested;
        _overlay.PreviewDismissed -= Reset;
        _hoverTimer.Tick -= OnHoverElapsed;
        _sourceTimer.Tick -= OnSourceTick;
        _overlay.Dispose();
        _audio.Dispose();
    }
}
