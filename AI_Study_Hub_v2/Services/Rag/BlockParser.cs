using System.Text.RegularExpressions;

namespace AI_Study_Hub_v2.Services.Rag;

public sealed class BlockParser
{
    private static readonly Regex HorizontalWhitespace = new(@"[ \t\f\v]+", RegexOptions.Compiled);
    private static readonly Regex BlankLineSplit = new(@"\n{2,}", RegexOptions.Compiled);
    private static readonly Regex NumberedHeading = new(
        @"^(chapter|section|part|chuong|muc|phan|bai)\b|^\d+(\.\d+)*[\)\.]?\s+\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ListMarker = new(
        @"^\s*(?:[-*+\u2022]|\d+[\.\)])\s+",
        RegexOptions.Compiled);

    public IReadOnlyList<TextBlock> Parse(IReadOnlyList<ExtractedPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var blocks = new List<TextBlock>();

        foreach (var page in pages)
        {
            var normalized = Normalize(page.Text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            foreach (var rawBlock in BlankLineSplit.Split(normalized))
            {
                var lines = rawBlock
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(line => HorizontalWhitespace.Replace(line.Trim(), " "))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();

                if (lines.Count == 0)
                {
                    continue;
                }

                var kind = ClassifyBlock(lines);
                var text = kind == TextBlockKind.List
                    ? string.Join('\n', lines)
                    : string.Join(' ', lines).Trim();

                blocks.Add(new TextBlock(page.PageNumber, text, kind));
            }
        }

        return blocks;
    }

    public bool IsHeading(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 120)
        {
            return false;
        }

        if (NumberedHeading.IsMatch(trimmed) || trimmed.EndsWith(':'))
        {
            return true;
        }

        if (trimmed.EndsWith('.') || trimmed.EndsWith('?') || trimmed.EndsWith('!'))
        {
            return false;
        }

        var letters = trimmed.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
        {
            return false;
        }

        var uppercaseRatio = letters.Count(char.IsUpper) / (double)letters.Length;
        if (uppercaseRatio >= 0.7)
        {
            return true;
        }

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length is > 0 and <= 8)
        {
            var titleCaseWords = words.Count(word => char.IsLetter(word[0]) && char.IsUpper(word[0]));
            return titleCaseWords == words.Length && titleCaseWords >= 2;
        }

        return false;
    }

    public bool IsListLine(string text) => ListMarker.IsMatch(text);

    private TextBlockKind ClassifyBlock(IReadOnlyList<string> lines)
    {
        if (lines.Count == 1 && IsHeading(lines[0]))
        {
            return TextBlockKind.Heading;
        }

        if (lines.All(IsListLine))
        {
            return TextBlockKind.List;
        }

        return TextBlockKind.Paragraph;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized
            .Split('\n')
            .Select(line => HorizontalWhitespace.Replace(line.Trim(), " "));

        return string.Join('\n', lines).Trim();
    }
}
