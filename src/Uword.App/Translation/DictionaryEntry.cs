using System.Text.Json;
using System.Text.RegularExpressions;

namespace Uword.App.Translation;

public enum TranslationMode { Translation, Dictionary }

public sealed record TranslationResult(string? Text, DictionaryEntry? Entry)
{
    public static TranslationResult Plain(string text) => new(text, null);
    public static TranslationResult Lexical(DictionaryEntry entry) => new(null, entry);
}

public sealed record DictionaryExample(string Source, string Translation);
public sealed record DictionarySense(string PartOfSpeech, IReadOnlyList<string> Meanings,
    IReadOnlyList<DictionaryExample> Examples);
public sealed record DictionaryEntry(string Headword, string? Phonetic, string Translation,
    IReadOnlyList<DictionarySense> Senses, string? Note)
{
    public static bool TryParse(string response, string source, out DictionaryEntry? entry)
    {
        entry = null;
        string json = response.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
            json = Regex.Replace(json, @"\A```(?:json)?\s*|\s*```\s*\z", "", RegexOptions.IgnoreCase).Trim();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var senses = new List<DictionarySense>();
            if (root.TryGetProperty("senses", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray().Take(4))
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var meanings = ReadStrings(item, "meanings", 6);
                    if (meanings.Count == 0) continue;
                    var examples = new List<DictionaryExample>();
                    if (item.TryGetProperty("examples", out var samples) && samples.ValueKind == JsonValueKind.Array)
                        foreach (var sample in samples.EnumerateArray().Take(2))
                        {
                            if (sample.ValueKind != JsonValueKind.Object) continue;
                            string original = Read(sample, "source", 300) ?? "";
                            string translated = Read(sample, "translation", 300) ?? "";
                            if (original.Length > 0 && translated.Length > 0)
                                examples.Add(new DictionaryExample(original, translated));
                        }
                    senses.Add(new DictionarySense(Read(item, "partOfSpeech", 20) ?? "",
                        meanings, examples));
                }
            string? translation = Read(root, "translation", 200);
            if (string.IsNullOrWhiteSpace(translation) && senses.Count == 0) return false;
            translation ??= senses[0].Meanings[0];
            entry = new DictionaryEntry(Read(root, "headword", 80) ?? source.Trim(),
                Read(root, "phonetic", 80), translation, senses, Read(root, "note", 600));
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement parent, string name, int limit) =>
        parent.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Take(limit).Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => (item.GetString() ?? "").Trim()).Where(value => value.Length > 0)
                .Select(value => value[..Math.Min(value.Length, 200)]).ToArray()
            : [];

    private static string? Read(JsonElement parent, string name, int limit)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        string text = (value.GetString() ?? "").Trim();
        return text.Length == 0 ? null : text[..Math.Min(text.Length, limit)];
    }
}

public static class WordCandidate
{
    private static readonly Regex Pattern = new(@"\A\p{L}[\p{L}\p{M}'’\-]*(?:[ \t]+\p{L}[\p{L}\p{M}'’\-]*){0,2}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsLikelyWord(string text) =>
        !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= 60 && Pattern.IsMatch(text.Trim());
}
