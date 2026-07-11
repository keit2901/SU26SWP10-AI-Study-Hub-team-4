using System.Text;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag;

public sealed class ChunkMerger
{
    private readonly int _minChunkChars;
    private readonly int _maxSectionChars;
    private readonly int _maxParagraphChars;

    public ChunkMerger(IOptions<RagOptions> options)
    {
        var value = options.Value;
        _minChunkChars = Math.Max(1, value.MinChunkChars);
        _maxSectionChars = Math.Max(_minChunkChars, value.MaxSectionChars);
        _maxParagraphChars = Math.Min(500, _maxSectionChars);
    }

    public IReadOnlyList<MergedChunk> Merge(IReadOnlyList<SplitBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var candidates = BuildCandidates(blocks);
        var mergedCandidates = MergeSmallCandidates(candidates);
        var overlappedCandidates = ApplyOverlap(mergedCandidates);

        return overlappedCandidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Content))
            .Select(candidate => new MergedChunk(
                candidate.PageNumber,
                candidate.Content.Trim(),
                candidate.SectionTitle,
                candidate.IsHeading))
            .ToList();
    }

    private List<ChunkCandidate> BuildCandidates(IReadOnlyList<SplitBlock> blocks)
    {
        var candidates = new List<ChunkCandidate>();
        string? currentSectionTitle = null;
        var nextParagraphStartsSection = false;

        foreach (var block in blocks)
        {
            if (block.Kind == TextBlockKind.Heading)
            {
                currentSectionTitle = block.Text;
                nextParagraphStartsSection = true;
                // Create a heading candidate so heading text appears in chunks.
                // Headings are short by nature and will merge forward with the
                // following paragraph in MergeSmallCandidates.
                candidates.Add(new ChunkCandidate(
                    PageNumber: block.PageNumber,
                    Content: block.Text.Trim(),
                    SectionTitle: currentSectionTitle,
                    IsHeading: true,
                    StartsNewParagraph: true,
                    StartsNewSection: true,
                    FirstSentence: null,
                    LastSentence: null,
                    ParagraphText: null));
                continue;
            }

            var blockCandidates = BuildParagraphCandidates(block, currentSectionTitle, nextParagraphStartsSection);
            if (blockCandidates.Count > 0)
            {
                candidates.AddRange(blockCandidates);
                nextParagraphStartsSection = false;
            }
        }

        return candidates;
    }

    private List<ChunkCandidate> BuildParagraphCandidates(
        SplitBlock block,
        string? sectionTitle,
        bool startsNewSection)
    {
        if (block.Units.Count == 0)
        {
            return new List<ChunkCandidate>();
        }

        var separator = block.Kind == TextBlockKind.List ? "\n" : " ";
        var paragraphText = string.Join(separator, block.Units).Trim();
        var candidates = new List<ChunkCandidate>();
        var current = new StringBuilder();
        var chunkUnits = new List<string>();

        foreach (var unit in block.Units)
        {
            var currentLength = current.Length;
            var projectedLength = currentLength == 0
                ? unit.Length
                : currentLength + separator.Length + unit.Length;

            if (currentLength > 0 && projectedLength > _maxParagraphChars && currentLength >= _minChunkChars)
            {
                candidates.Add(CreateParagraphCandidate(
                    block.PageNumber,
                    sectionTitle,
                    startsNewParagraph: candidates.Count == 0,
                    startsNewSection: startsNewSection && candidates.Count == 0,
                    paragraphText,
                    chunkUnits,
                    current.ToString()));

                current.Clear();
                chunkUnits.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(separator);
            }

            current.Append(unit);
            chunkUnits.Add(unit);
        }

        if (current.Length > 0)
        {
            candidates.Add(CreateParagraphCandidate(
                block.PageNumber,
                sectionTitle,
                startsNewParagraph: candidates.Count == 0,
                startsNewSection: startsNewSection && candidates.Count == 0,
                paragraphText,
                chunkUnits,
                current.ToString()));
        }

        if (candidates.Count >= 2)
        {
            var last = candidates[^1];
            var previous = candidates[^2];
            var merged = $"{previous.Content} {last.Content}".Trim();

            if (last.Content.Length < _minChunkChars && merged.Length <= _maxSectionChars)
            {
                candidates[^2] = previous with
                {
                    Content = merged,
                    LastSentence = last.LastSentence,
                };
                candidates.RemoveAt(candidates.Count - 1);
            }
        }

        return candidates;
    }

    private static ChunkCandidate CreateParagraphCandidate(
        int? pageNumber,
        string? sectionTitle,
        bool startsNewParagraph,
        bool startsNewSection,
        string paragraphText,
        IReadOnlyList<string> chunkUnits,
        string content)
    {
        return new ChunkCandidate(
            PageNumber: pageNumber,
            Content: content.Trim(),
            SectionTitle: sectionTitle,
            IsHeading: false,
            StartsNewParagraph: startsNewParagraph,
            StartsNewSection: startsNewSection,
            FirstSentence: chunkUnits.FirstOrDefault(),
            LastSentence: chunkUnits.LastOrDefault(),
            ParagraphText: paragraphText);
    }

    private List<ChunkCandidate> MergeSmallCandidates(IReadOnlyList<ChunkCandidate> candidates)
    {
        var working = new List<ChunkCandidate>(candidates);
        var merged = new List<ChunkCandidate>(working.Count);

        for (var i = 0; i < working.Count; i++)
        {
            var current = working[i];
            if (current.Content.Length >= _minChunkChars)
            {
                merged.Add(current);
                continue;
            }

            if (i + 1 >= working.Count
                || working[i + 1].IsHeading
                || (!current.IsHeading && working[i + 1].StartsNewSection)
                || !string.Equals(current.SectionTitle, working[i + 1].SectionTitle, StringComparison.Ordinal))
            {
                merged.Add(current);
                continue;
            }

            var next = working[i + 1];
            var mergedContent = $"{current.Content} {next.Content}".Trim();
            if (mergedContent.Length > _maxSectionChars)
            {
                merged.Add(current);
                continue;
            }

            working[i + 1] = next with
            {
                PageNumber = current.PageNumber ?? next.PageNumber,
                Content = mergedContent,
                SectionTitle = current.SectionTitle ?? next.SectionTitle,
                StartsNewParagraph = current.StartsNewParagraph || next.StartsNewParagraph,
                StartsNewSection = current.StartsNewSection || next.StartsNewSection,
                FirstSentence = current.FirstSentence ?? next.FirstSentence,
                ParagraphText = string.IsNullOrWhiteSpace(next.ParagraphText) ? current.ParagraphText : next.ParagraphText,
            };
        }

        return merged;
    }

    private List<ChunkCandidate> ApplyOverlap(IReadOnlyList<ChunkCandidate> candidates)
    {
        var overlapped = new List<ChunkCandidate>(candidates.Count);
        string? previousParagraphSentence = null;
        string? previousSectionParagraph = null;
        string? currentSectionLastParagraph = null;

        foreach (var candidate in candidates)
        {
            if (candidate.IsHeading)
            {
                if (!string.IsNullOrWhiteSpace(currentSectionLastParagraph))
                {
                    previousSectionParagraph = currentSectionLastParagraph;
                }

                currentSectionLastParagraph = null;
                overlapped.Add(candidate);
                continue;
            }

            if (candidate.StartsNewSection)
            {
                if (!string.IsNullOrWhiteSpace(currentSectionLastParagraph))
                {
                    previousSectionParagraph = currentSectionLastParagraph;
                }

                currentSectionLastParagraph = null;
            }

            var prefix = candidate.StartsNewSection
                ? previousSectionParagraph
                : candidate.StartsNewParagraph
                    ? previousParagraphSentence
                    : null;

            var content = CombineWithOverlap(prefix, candidate.Content);
            currentSectionLastParagraph = candidate.ParagraphText;
            previousParagraphSentence = candidate.LastSentence;

            overlapped.Add(candidate with { Content = content });
        }

        return overlapped;
    }

    private string CombineWithOverlap(string? prefix, string content)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return content;
        }

        var combined = $"{prefix.Trim()} {content}".Trim();
        if (combined.Length <= _maxSectionChars)
        {
            return combined;
        }

        var lastSentence = ExtractLastSentence(prefix);
        if (!string.IsNullOrWhiteSpace(lastSentence))
        {
            var shorterOverlap = $"{lastSentence} {content}".Trim();
            if (shorterOverlap.Length <= _maxSectionChars)
            {
                return shorterOverlap;
            }
        }

        return content;
    }

    private static string? ExtractLastSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var separators = new[] { ". ", "? ", "! ", "; " };
        foreach (var separator in separators)
        {
            var index = text.LastIndexOf(separator, StringComparison.Ordinal);
            if (index >= 0 && index + separator.Length < text.Length)
            {
                return text[(index + separator.Length)..].Trim();
            }
        }

        return text.Trim();
    }

    private sealed record ChunkCandidate(
        int? PageNumber,
        string Content,
        string? SectionTitle,
        bool IsHeading,
        bool StartsNewParagraph,
        bool StartsNewSection,
        string? FirstSentence,
        string? LastSentence,
        string? ParagraphText)
    {
    }
}
