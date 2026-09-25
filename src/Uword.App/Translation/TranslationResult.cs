using System.Text.Json;
using System.Text.RegularExpressions;

namespace Uword.App.Translation;

public sealed record TranslationResult(string Translation, string? ExampleSource = null,
    string? ExampleTranslation = null)
{
    public static bool TryParse(string response, out TranslationResult? result)
    {
        result = null;
        string json = response.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
            json = Regex.Replace(json, @"\A```(?:json)?\s*|\s*```\s*\z", "", RegexOptions.IgnoreCase).Trim();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            string? translation = Read(root, "translation", 100_000);
            if (translation is null) return false;
            string? source = Read(root, "exampleSource", 500);
            string? exampleTranslation = Read(root, "exampleTranslation", 500);
            result = new TranslationResult(translation, source is not null && exampleTranslation is not null ? source : null,
                source is not null && exampleTranslation is not null ? exampleTranslation : null);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static string? Read(JsonElement root, string name, int limit)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        string text = (value.GetString() ?? "").Trim();
        return text.Length == 0 ? null : text[..Math.Min(text.Length, limit)];
    }
}
