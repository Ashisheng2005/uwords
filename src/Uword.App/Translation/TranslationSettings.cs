using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Uword.App.Translation;

public sealed record TranslationSettings
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "";
    public string TargetLanguage { get; set; } = "简体中文";
    public double Temperature { get; set; }
    public int RequestsPerSecond { get; set; } = 2;
    public int MaxTextLength { get; set; } = 1200;
    public int MaxParagraphs { get; set; } = 4;
    public string SystemPrompt { get; set; } = """
        You are a professional {{to}} native translator who needs to fluently translate text into {{to}}.

        Translation rules:
        1. Output only the translation, without explanations or introductions.
        2. Preserve exactly the same number of paragraphs and the original formatting.
        3. Preserve HTML tags in appropriate positions while maintaining fluency.
        4. Keep proper nouns, code, and other content that should not be translated unchanged.
        5. If the input contains %% as a paragraph separator, use %% in your output. Otherwise do not use %%.

        Output format:
        - Single paragraph: output the translation directly, without a separator.
        - Multiple paragraphs: output one translation per input paragraph, separated by a line containing only %%.
        """;
    public string MultiParagraphPrompt { get; set; } = "Translate to {{to}}:\n\n{{text}}";
    public string SingleParagraphPrompt { get; set; } = "Translate to {{to}} (output translation only):\n\n{{text}}";
    public string DictionaryPrompt { get; set; } = """
        Explain the selected word or short phrase "{{text}}" in {{to}}.
        Return ONLY one JSON object with keys: headword (string), phonetic (IPA string or null if uncertain),
        translation (short string), senses (array of objects with partOfSpeech, meanings as a string array,
        examples as an array of objects with source and translation strings), note (short string or null).
        Group distinct meanings by part of speech, include at most 3 senses and 2 short bilingual examples per sense.
        Examples you create are illustrative, not quotations. Do not invent a phonetic spelling if unsure.
        """;

    [JsonIgnore]
    public string ApiKey { get; set; } = "";

    public Uri Endpoint()
    {
        if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new ArgumentException("Base URL 必须是 HTTPS 地址（本机 localhost 可使用 HTTP）。");
        if (uri.AbsolutePath.TrimEnd('/').EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return uri;
        return new Uri(uri.ToString().TrimEnd('/') + "/chat/completions");
    }

    public void Validate(bool requireKey = true)
    {
        _ = Endpoint();
        if (string.IsNullOrWhiteSpace(Model) || string.IsNullOrWhiteSpace(TargetLanguage))
            throw new ArgumentException("请填写模型名称和目标语言。");
        if (requireKey && string.IsNullOrWhiteSpace(ApiKey))
            throw new ArgumentException("请填写 API Key。");
        if (!double.IsFinite(Temperature) || Temperature < 0 || Temperature > 2)
            throw new ArgumentException("温度须介于 0 和 2 之间。");
        if (RequestsPerSecond is < 1 or > 100 || MaxTextLength is < 1 or > 100_000 || MaxParagraphs is < 1 or > 100)
            throw new ArgumentException("请求速率、文本长度和段落数须为有效正整数。");
        ValidatePrompt(SystemPrompt, false);
        if (SystemPrompt.Contains("{{text}}", StringComparison.Ordinal))
            throw new ArgumentException("{{text}} 仅可用于单段和多段提示词。");
        ValidatePrompt(MultiParagraphPrompt, true);
        ValidatePrompt(SingleParagraphPrompt, true);
        ValidatePrompt(DictionaryPrompt, true);
    }

    private static void ValidatePrompt(string prompt, bool requireText)
    {
        if (string.IsNullOrWhiteSpace(prompt) ||
            (requireText && !prompt.Contains("{{text}}", StringComparison.Ordinal)))
            throw new ArgumentException("提示词不能为空，单段和多段提示词须包含 {{text}}。");
        foreach (Match match in Regex.Matches(prompt, @"\{\{([^{}]+)\}\}"))
            if (match.Groups[1].Value is not ("to" or "text"))
                throw new ArgumentException($"不支持提示词变量 {match.Value}；仅支持 {{{{to}}}} 和 {{{{text}}}}。");
    }

    public static string Render(string template, string language, string text) =>
        template.Replace("{{to}}", language, StringComparison.Ordinal)
            .Replace("{{text}}", text, StringComparison.Ordinal);
}
