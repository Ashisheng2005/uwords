using System.Text;
using System.Text.RegularExpressions;

namespace Uword.App.Translation;

public sealed class TranslationPlan
{
    public sealed record Piece(int Paragraph, string Text);
    public sealed record Batch(IReadOnlyList<Piece> Pieces);

    private readonly string[] _separators;
    public IReadOnlyList<Batch> Batches { get; }

    private TranslationPlan(string[] separators, IReadOnlyList<Batch> batches)
    {
        _separators = separators;
        Batches = batches;
    }

    public static TranslationPlan Create(string text, int maxLength, int maxParagraphs)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("选中文本为空。");
        if (maxLength < 1 || maxParagraphs < 1) throw new ArgumentOutOfRangeException(nameof(maxLength));
        var tokens = Regex.Split(text, @"(\r?\n[ \t]*\r?\n(?:[ \t]*\r?\n)*)");
        var separators = new List<string>();
        var pieces = new List<Piece>();
        for (int i = 0; i < tokens.Length; i += 2)
        {
            int index = separators.Count;
            foreach (string part in SplitLong(tokens[i], maxLength)) pieces.Add(new Piece(index, part));
            if (i + 1 < tokens.Length) separators.Add(tokens[i + 1]);
        }
        var batches = new List<Batch>();
        var current = new List<Piece>();
        int length = 0;
        bool hasDelimiter = Regex.IsMatch(text, @"(?m)^[ \t]*%%[ \t]*\r?$");
        foreach (Piece piece in pieces)
        {
            if (current.Count > 0 && (hasDelimiter || current.Count >= maxParagraphs ||
                length + 6 + piece.Text.Length > maxLength || current[^1].Paragraph == piece.Paragraph))
            {
                batches.Add(new Batch(current.ToArray()));
                current.Clear();
                length = 0;
            }
            current.Add(piece);
            length += piece.Text.Length + (current.Count > 1 ? 6 : 0);
        }
        if (current.Count > 0) batches.Add(new Batch(current.ToArray()));
        return new TranslationPlan(separators.ToArray(), batches);
    }

    public string Assemble(IReadOnlyList<string> results)
    {
        var paragraphs = new List<string>();
        int part = 0;
        foreach (Batch batch in Batches)
            foreach (Piece piece in batch.Pieces)
            {
                while (paragraphs.Count <= piece.Paragraph) paragraphs.Add("");
                paragraphs[piece.Paragraph] += (paragraphs[piece.Paragraph].Length == 0 ? "" : " ") + results[part++];
            }
        var output = new StringBuilder();
        for (int i = 0; i < paragraphs.Count; i++)
        {
            if (i > 0) output.Append(_separators[i - 1]);
            output.Append(paragraphs[i]);
        }
        if (_separators.Length >= paragraphs.Count && _separators.Length > 0)
            output.Append(_separators[^1]);
        return output.ToString();
    }

    private static IEnumerable<string> SplitLong(string text, int maxLength)
    {
        while (text.Length > maxLength)
        {
            int split = maxLength;
            for (int i = maxLength - 1; i >= maxLength / 2; i--)
                if (char.IsWhiteSpace(text[i]) || ".!?。！？;；".Contains(text[i]))
                { split = i + 1; break; }
            if (split < text.Length && char.IsHighSurrogate(text[split - 1])) split--;
            if (split == 0) throw new ArgumentException("每次最大字数不足以容纳一个 Unicode 字符。");
            yield return text[..split];
            text = text[split..];
        }
        if (text.Length > 0) yield return text;
    }

    public static string[] Parse(string content, int expected)
    {
        string[] items = expected == 1 ? [content.Trim()] :
            Regex.Split(content.Trim(), @"(?m)^[ \t]*%%[ \t]*\r?$")
                .Select(value => value.Trim()).ToArray();
        if (items.Length != expected || items.Any(string.IsNullOrWhiteSpace))
            throw new FormatException("译文段落数量与原文不一致。");
        return items;
    }
}
