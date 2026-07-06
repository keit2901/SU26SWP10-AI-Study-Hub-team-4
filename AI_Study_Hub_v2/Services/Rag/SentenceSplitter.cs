using System.Text.RegularExpressions;

namespace AI_Study_Hub_v2.Services.Rag;

public sealed class SentenceSplitter
{
    private static readonly Regex HorizontalWhitespace = new(@"[ \t\f\v]+", RegexOptions.Compiled);
    private static readonly Regex ListMarker = new(
        @"^\s*(?:[-*+\u2022]|\d+[\.\)])\s+",
        RegexOptions.Compiled);
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr.", "mrs.", "ms.", "dr.", "prof.", "vs.", "etc.", "e.g.", "i.e.",
        "ths.", "ths", "ts.", "ts", "pgs.", "pgs", "gs.", "gs", "tp.", "q."
    };

    public IReadOnlyList<SplitBlock> Split(IReadOnlyList<TextBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        return blocks
            .Select(block => new SplitBlock(
                block.PageNumber,
                block.Text,
                block.Kind,
                block.Kind == TextBlockKind.List
                    ? SplitListItems(block.Text)
                    : SplitIntoSentences(block.Text)))
            .Where(block => block.Units.Count > 0 || block.Kind == TextBlockKind.Heading)
            .ToList();
    }

    public IReadOnlyList<string> SplitIntoSentences(string text)
    {
        var normalized = HorizontalWhitespace.Replace(text.Replace("\r\n", "\n").Replace('\r', '\n').Trim(), " ");
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<string>();
        }

        var sentences = new List<string>();
        var start = 0;

        for (var i = 0; i < normalized.Length; i++)
        {
            if (!IsSentenceBoundary(normalized, i))
            {
                continue;
            }

            var end = i + 1;
            while (end < normalized.Length && "\"')]}".Contains(normalized[end]))
            {
                end++;
            }

            var sentence = normalized[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                sentences.Add(sentence);
            }

            start = SkipWhitespace(normalized, end);
            i = start - 1;
        }

        if (start < normalized.Length)
        {
            var trailing = normalized[start..].Trim();
            if (!string.IsNullOrWhiteSpace(trailing))
            {
                sentences.Add(trailing);
            }
        }

        return sentences.Count == 0 ? new[] { normalized } : sentences;
    }

    public IReadOnlyList<string> SplitListItems(string text)
    {
        return text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => ListMarker.Replace(line, string.Empty).Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private bool IsSentenceBoundary(string text, int index)
    {
        var current = text[index];
        if (current is not ('.' or '?' or '!' or ';'))
        {
            return false;
        }

        if (current == '.')
        {
            if (index > 0 && index < text.Length - 1 && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]))
            {
                return false;
            }

            var token = ReadTokenBefore(text, index);
            if (Abbreviations.Contains(token))
            {
                return false;
            }
        }

        var nextIndex = SkipWhitespace(text, index + 1);
        return nextIndex >= text.Length || char.IsLetterOrDigit(text[nextIndex]) || "\"'([{".Contains(text[nextIndex]);
    }

    private static string ReadTokenBefore(string text, int endExclusive)
    {
        var start = endExclusive;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        return text[start..(endExclusive + 1)].Trim();
    }

    private static int SkipWhitespace(string text, int start)
    {
        var index = start;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }
}
