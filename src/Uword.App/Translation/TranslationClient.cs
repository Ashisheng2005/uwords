using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Uword.App.Translation;

public sealed class TranslationClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _rateGate = new(1, 1);
    private DateTimeOffset _nextRequest;

    public TranslationClient(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
    }

    public async Task<string> TranslateAsync(string text, TranslationSettings settings, CancellationToken token)
    {
        settings.Validate();
        var plan = TranslationPlan.Create(text, settings.MaxTextLength, settings.MaxParagraphs);
        var results = new List<string>();
        foreach (var batch in plan.Batches)
        {
            token.ThrowIfCancellationRequested();
            string[] parsed;
            try
            {
                string joined = string.Join("\n\n%%\n\n", batch.Pieces.Select(p => p.Text));
                string content = await CompleteAsync(joined, batch.Pieces.Count > 1, settings, token);
                if (batch.Pieces.Count == 1 && !joined.Contains("%%", StringComparison.Ordinal) &&
                    content.Contains("%%", StringComparison.Ordinal))
                    throw new FormatException("单段译文包含意外的 %% 分隔符。");
                parsed = TranslationPlan.Parse(content, batch.Pieces.Count);
            }
            catch (FormatException) when (batch.Pieces.Count > 1)
            {
                // Recover a malformed multi-paragraph response without resending other batches.
                parsed = new string[batch.Pieces.Count];
                for (int i = 0; i < parsed.Length; i++)
                    parsed[i] = TranslationPlan.Parse(
                        await CompleteAsync(batch.Pieces[i].Text, false, settings, token), 1)[0];
            }
            results.AddRange(parsed);
        }
        return plan.Assemble(results);
    }

    public async Task<TranslationResult> TranslateRichAsync(string text, TranslationMode mode,
        TranslationSettings settings, CancellationToken token)
    {
        if (mode != TranslationMode.Dictionary || !WordCandidate.IsLikelyWord(text) ||
            text.Trim().Length > settings.MaxTextLength)
            return TranslationResult.Plain(await TranslateAsync(text, settings, token));

        settings.Validate();
        const string system = "You are a bilingual dictionary editor. Reply with one valid JSON object only. " +
            "Use null for an unknown IPA transcription; never fabricate citations or source context.";
        string content = await CompleteAsync(text.Trim(), false, settings, token,
            settings.DictionaryPrompt, system);
        if (DictionaryEntry.TryParse(content, text, out var entry))
            return TranslationResult.Lexical(entry!);

        // A plain answer is usable immediately; malformed JSON needs a normal translation request.
        if (!content.TrimStart().StartsWith('{') && !content.TrimStart().StartsWith("```", StringComparison.Ordinal))
            return TranslationResult.Plain(content.Trim());
        return TranslationResult.Plain(await TranslateAsync(text, settings, token));
    }

    public Task<string> TestAsync(TranslationSettings settings, CancellationToken token)
    {
        settings.Validate();
        const string sample = "Hello, world!";
        return CompleteAsync(sample[..Math.Min(sample.Length, settings.MaxTextLength)], false, settings, token);
    }

    private async Task<string> CompleteAsync(string text, bool multi, TranslationSettings settings, CancellationToken token,
        string? userTemplate = null, string? systemTemplate = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = settings.Model.Trim(),
            temperature = settings.Temperature,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = TranslationSettings.Render(systemTemplate ?? settings.SystemPrompt, settings.TargetLanguage, "") },
                new { role = "user", content = TranslationSettings.Render(
                    userTemplate ?? (multi ? settings.MultiParagraphPrompt : settings.SingleParagraphPrompt), settings.TargetLanguage, text) }
            }
        });

        for (int attempt = 0; ; attempt++)
        {
            await ReserveAsync(settings.RequestsPerSecond, token);
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            HttpResponseMessage received;
            try { received = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); }
            catch (HttpRequestException) when (attempt < 2 && !token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), token);
                continue;
            }
            using var response = received;
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 2)
            {
                TimeSpan retry = response.Headers.RetryAfter?.Delta ??
                    (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
                    TimeSpan.FromMilliseconds(500 * (attempt + 1));
                await Task.Delay(retry > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) :
                    retry < TimeSpan.Zero ? TimeSpan.Zero : retry, token);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new TranslationRequestException($"翻译服务返回 HTTP {(int)response.StatusCode}。", (int)response.StatusCode);
            await using var body = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: token);
            try
            {
                var choice = json.RootElement.GetProperty("choices")[0];
                string? reason = choice.GetProperty("finish_reason").GetString();
                if (reason != "stop")
                    throw new TranslationRequestException("翻译未正常完成（finish_reason 不为 stop）。");
                string? content = choice.GetProperty("message").GetProperty("content").GetString();
                if (string.IsNullOrWhiteSpace(content)) throw new FormatException("翻译服务返回空内容。");
                return content;
            }
            catch (KeyNotFoundException) { throw new FormatException("翻译服务响应缺少 choices/message/content。"); }
            catch (IndexOutOfRangeException) { throw new FormatException("翻译服务返回空 choices。"); }
            catch (InvalidOperationException) { throw new FormatException("翻译服务响应格式错误。"); }
        }
    }

    private async Task ReserveAsync(int perSecond, CancellationToken token)
    {
        await _rateGate.WaitAsync(token);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (_nextRequest > now) await Task.Delay(_nextRequest - now, token);
            _nextRequest = DateTimeOffset.UtcNow.AddSeconds(1.0 / perSecond);
        }
        finally { _rateGate.Release(); }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
        _rateGate.Dispose();
    }
}

public sealed class TranslationRequestException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}
